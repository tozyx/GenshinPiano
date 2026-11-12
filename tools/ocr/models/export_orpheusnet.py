"""Export the MIT-licensed OrpheusNet classifiers to portable ONNX files."""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

import numpy as np
import onnx
from onnx.reference import ReferenceEvaluator
import torch


def export_model(model: torch.nn.Module, output_path: Path) -> None:
    model.eval()
    sample = torch.linspace(-1.0, 1.0, 28 * 28).reshape(1, 1, 28, 28)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    torch.onnx.export(
        model,
        sample,
        output_path,
        input_names=["image"],
        output_names=["logits"],
        dynamic_axes={"image": {0: "batch"}, "logits": {0: "batch"}},
        opset_version=17,
        do_constant_folding=True,
    )

    expected = model(sample).detach().numpy()
    session = ReferenceEvaluator(onnx.load(output_path))
    actual = session.run(["logits"], {"image": sample.numpy()})[0]
    max_error = float(np.max(np.abs(expected - actual)))
    if max_error > 1e-4:
        raise RuntimeError(f"ONNX verification failed: max error {max_error:g}")
    print(f"Exported {output_path.name}; max error={max_error:g}")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path, help="Path to the OrpheusNet repository")
    parser.add_argument("output", type=Path, help="Destination model directory")
    args = parser.parse_args()

    source = args.source.resolve()
    sys.path.insert(0, str(source))
    from CNNmodel import CNN_meta, CNN_middle  # pylint: disable=import-error,import-outside-toplevel

    middle = CNN_middle(output_size=13)
    middle.load_state_dict(
        torch.load(source / "pths" / "CNN_middle.pth", map_location="cpu", weights_only=True)
    )
    export_model(middle, args.output / "orpheusnet-middle.onnx")

    metadata = CNN_meta(output_size=20)
    metadata.load_state_dict(
        torch.load(source / "pths" / "CNN_meta.pth", map_location="cpu", weights_only=True)
    )
    export_model(metadata, args.output / "orpheusnet-metadata.onnx")


if __name__ == "__main__":
    main()
