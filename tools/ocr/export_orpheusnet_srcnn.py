"""Export OrpheusNet's trusted SRCNN x3 checkpoint to portable ONNX."""

from __future__ import annotations

import argparse
import sys
import types
from pathlib import Path

import numpy as np
import onnx
from onnx.reference import ReferenceEvaluator
import torch
from torch import nn
from torch.nn import functional as F


class SRCNN(nn.Module):
    def __init__(self) -> None:
        super().__init__()
        self.conv1 = nn.Conv2d(1, 64, kernel_size=9, padding=4)
        self.conv2 = nn.Conv2d(64, 32, kernel_size=1)
        self.conv3 = nn.Conv2d(32, 1, kernel_size=5, padding=2)

    def forward(self, image: torch.Tensor) -> torch.Tensor:
        image = F.relu(self.conv1(image))
        image = F.relu(self.conv2(image))
        return self.conv3(image)


def load_checkpoint(path: Path) -> SRCNN:
    # The upstream checkpoint stores the complete model and therefore refers to
    # model.SRCNN. Register only that known type before loading this trusted,
    # repository-bundled checkpoint.
    module = types.ModuleType("model")
    SRCNN.__module__ = "model"
    module.SRCNN = SRCNN
    sys.modules["model"] = module
    loaded = torch.load(path, map_location="cpu", weights_only=False)
    if not isinstance(loaded, SRCNN):
        raise TypeError(f"Unexpected checkpoint type: {type(loaded)!r}")
    return loaded.eval()


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("checkpoint", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()

    model = load_checkpoint(args.checkpoint.resolve())
    sample = torch.linspace(0.0, 1.0, 48 * 72).reshape(1, 1, 48, 72)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    torch.onnx.export(
        model,
        sample,
        args.output,
        input_names=["image"],
        output_names=["enhanced"],
        dynamic_axes={
            "image": {0: "batch", 2: "height", 3: "width"},
            "enhanced": {0: "batch", 2: "height", 3: "width"},
        },
        opset_version=17,
        do_constant_folding=True,
    )

    expected = model(sample).detach().numpy()
    actual = ReferenceEvaluator(onnx.load(args.output)).run(
        ["enhanced"], {"image": sample.numpy()}
    )[0]
    max_error = float(np.max(np.abs(expected - actual)))
    if max_error > 1e-4:
        raise RuntimeError(f"ONNX verification failed: max error={max_error:g}")
    print(f"Exported {args.output}; max error={max_error:g}")


if __name__ == "__main__":
    main()
