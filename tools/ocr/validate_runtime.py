"""Check the portable inference runtime without installing system packages."""

import json
from pathlib import Path
from tempfile import TemporaryDirectory

import cv2
import numpy as np
import oemer
import onnxruntime as ort
from oemer import ete
from scipy.signal import find_peaks


def main() -> None:
    # OCR needs still-image codecs and geometry operations, not video decoding.
    with TemporaryDirectory() as temporary:
        image = np.full((32, 48), 255, dtype=np.uint8)
        image[10:15, 8:30] = 0
        path = str(Path(temporary) / "score.png")
        assert cv2.imwrite(path, image)
        loaded = cv2.imread(path, cv2.IMREAD_GRAYSCALE)
        np.testing.assert_array_equal(loaded, image)
        assert cv2.connectedComponents(255 - loaded)[0] == 2
        assert cv2.resize(loaded, (24, 16)).shape == (16, 24)
        assert find_peaks(np.array([0, 1, 0]))[0].tolist() == [1]

    root = Path(oemer.__file__).parent / "checkpoints"
    sessions = [
        ort.InferenceSession(str(root / name / "model.onnx"), providers=["CPUExecutionProvider"])
        for name in ("unet_big", "seg_net")
    ]
    for session in sessions:
        model_input = session.get_inputs()[0]
        shape = [1, *model_input.shape[1:]]
        predictions = session.run(None, {model_input.name: np.zeros(shape, dtype=np.uint8)})
        assert predictions and all(np.isfinite(output).all() for output in predictions)
    print(json.dumps({
        "oemer": str(Path(oemer.__file__).parent),
        "ort": ort.__version__,
        "providers": ort.get_available_providers(),
        "modelsCpuOnly": all(session.get_providers() == ["CPUExecutionProvider"] for session in sessions),
    }))


if __name__ == "__main__":
    main()
