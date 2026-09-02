import argparse
import os
import sys

import numpy as np
from scipy.signal import find_peaks


def configure_execution_provider(prefer_gpu: bool) -> None:
    """Use CUDA when requested and healthy, otherwise transparently use CPU."""
    import onnxruntime as runtime

    available = runtime.get_available_providers()
    use_cuda = prefer_gpu and "CUDAExecutionProvider" in available
    desired = (
        [("CUDAExecutionProvider", {"device_id": 0}), "CPUExecutionProvider"]
        if use_cuda
        else ["CPUExecutionProvider"]
    )
    original_session = runtime.InferenceSession
    reported = False

    def safe_session(*args, **kwargs):
        nonlocal reported, use_cuda, desired
        kwargs["providers"] = desired
        try:
            session = original_session(*args, **kwargs)
        except Exception as exception:
            if not use_cuda:
                raise
            use_cuda = False
            desired = ["CPUExecutionProvider"]
            print(
                f"OCR_BACKEND|CPU|CUDA initialization failed: {exception}",
                file=sys.stderr,
                flush=True,
            )
            reported = True
            kwargs["providers"] = desired
            session = original_session(*args, **kwargs)
        if use_cuda and "CUDAExecutionProvider" not in session.get_providers():
            use_cuda = False
            desired = ["CPUExecutionProvider"]
        if not reported:
            reason = "" if use_cuda else ("|CUDA unavailable" if prefer_gpu else "|GPU disabled")
            backend_name = "CUDA" if use_cuda else "CPU"
            print(f"OCR_BACKEND|{backend_name}{reason}", flush=True)
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
    parser.add_argument("--use-gpu", action="store_true")
    args = parser.parse_args()

    from oemer import MODULE_PATH
    from oemer.ete import clear_data, extract

    required = {
        os.path.join(MODULE_PATH, "checkpoints", "unet_big", "model.onnx"): 70767752,
        os.path.join(MODULE_PATH, "checkpoints", "seg_net", "model.onnx"): 38448467,
    }
    missing = [
        path for path, expected_size in required.items()
        if not os.path.isfile(path) or os.path.getsize(path) != expected_size
    ]
    if missing:
        print("Missing oemer ONNX checkpoints: " + ", ".join(missing), file=sys.stderr)
        return 4

    configure_execution_provider(args.use_gpu)
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
