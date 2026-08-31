# Third-party notices

## OrpheusNet

The optional numbered-notation OCR add-on includes neural-network weights
derived from [OrpheusNet](https://github.com/Akane0721/OrpheusNet).

Copyright (c) 2024 Akane0721. Licensed under the MIT License. A copy of the
license and the upstream source are available in the linked repository.

The weights were converted from PyTorch `.pth` files to ONNX without changing
their learned parameters. GenshinPiano's preprocessing, page segmentation and
score reconstruction are separate implementations.

The optional low-resolution enhancement branch uses the SRCNN x3 checkpoint
distributed by OrpheusNet. It is converted to ONNX without changing its learned
parameters and is only evaluated for small glyph crops. Recognition results
that disagree with the original image branch are not adopted automatically.
