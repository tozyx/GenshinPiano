# Third-party notices

## NAudio

The desktop application's sampled-instrument mixer uses
[NAudio](https://github.com/naudio/NAudio) 2.2.1.

Copyright 2020 Mark Heath

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
of the Software, and to permit persons to whom the Software is furnished to do
so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

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
