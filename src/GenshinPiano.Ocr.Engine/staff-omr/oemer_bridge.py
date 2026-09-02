import argparse
import os
import sys

import numpy as np
from scipy.signal import find_peaks


def configure_execution_provider() -> None:
    """Force the packaged CPU runtime for predictable deployment."""
    import onnxruntime as runtime

    original_session = runtime.InferenceSession
    reported = False

    def safe_session(*args, **kwargs):
        nonlocal reported
        kwargs["providers"] = ["CPUExecutionProvider"]
        session = original_session(*args, **kwargs)
        if not reported:
            print("OCR_BACKEND|CPU", flush=True)
            reported = True
        return session

    runtime.InferenceSession = safe_session


def install_oemer_compatibility_fixes() -> None:
    """Skip blank horizontal zones instead of triggering an upstream crash."""
    from oemer import staffline_extraction

    original_extract_line = staffline_extraction.extract_line

    def safe_extract_line(pred, x_offset, line_threshold=0.8):
        count = np.bincount(np.where(pred > 0)[0], minlength=len(pred))
        padded = np.insert(count, [0, len(count)], [0, 0])
        std = np.std(padded)
        if std == 0:
            return np.array([], dtype=object), np.zeros(len(pred))

        norm = (padded - np.mean(padded)) / std
        centers, _ = find_peaks(
            norm,
            height=line_threshold,
            distance=8,
            prominence=1,
        )
        if len(centers) < 5:
            return np.array([], dtype=object), norm[1:-1]

        return original_extract_line(pred, x_offset, line_threshold)

    staffline_extraction.extract_line = safe_extract_line


def main() -> int:
    parser = argparse.ArgumentParser(description="GenshinPiano oemer bridge")
    parser.add_argument("image_path")
    parser.add_argument("--output-path", required=True)
    parser.add_argument("--without-deskew", action="store_true")
    args = parser.parse_args()

    from oemer import MODULE_PATH
    from oemer import ete

    bundled_root = os.path.dirname(os.path.abspath(__file__))
    bundled_checkpoints = os.path.join(bundled_root, "checkpoints")
    model_root = bundled_root if os.path.isdir(bundled_checkpoints) else MODULE_PATH
    # oemer.ete imports MODULE_PATH by value, so point that module at the
    # add-on-local checkpoints while leaving inference''s sklearn model path
    # attached to the installed Python package.
    ete.MODULE_PATH = model_root
    clear_data = ete.clear_data
    extract = ete.extract

    required = {
        os.path.join(model_root, "checkpoints", "unet_big", "model.onnx"): 70767752,
        os.path.join(model_root, "checkpoints", "seg_net", "model.onnx"): 38448467,
    }
    missing = [
        path for path, expected_size in required.items()
        if not os.path.isfile(path) or os.path.getsize(path) != expected_size
    ]
    if missing:
        print("Missing oemer ONNX checkpoints: " + ", ".join(missing), file=sys.stderr)
        return 4

    configure_execution_provider()
    install_oemer_compatibility_fixes()
    clear_data()
    backend_args = argparse.Namespace(
        img_path=args.image_path,
        output_path=args.output_path,
        use_tf=False,
        save_cache=False,
        without_deskew=args.without_deskew,
    )
    output = extract(backend_args)
    print(f"MusicXML written to {output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
