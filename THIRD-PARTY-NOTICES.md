# Third-party notices

These are the NuGet packages shipped with PeritumAI Protect. Versions, licenses, and copyright lines are taken from each package in the local NuGet cache (`%USERPROFILE%\.nuget\packages`). Test-only packages are not listed.

PdfPig 0.1.16 is one package. It contains these assemblies: UglyToad.PdfPig, UglyToad.PdfPig.Core, UglyToad.PdfPig.DocumentLayoutAnalysis, UglyToad.PdfPig.Fonts, UglyToad.PdfPig.Package, UglyToad.PdfPig.Tokenization, and UglyToad.PdfPig.Tokens. It does not ship separate sub-packages.

| Package | Version | License | Copyright |
| --- | --- | --- | --- |
| DocumentFormat.OpenXml | 3.1.1 | MIT (nuspec license expression; the package has no license file) | © Microsoft Corporation. All rights reserved. |
| DocumentFormat.OpenXml.Framework | 3.1.1 | MIT (nuspec license expression; the package has no license file) | © Microsoft Corporation. All rights reserved. |
| System.IO.Packaging | 8.0.1 | MIT | © Microsoft Corporation. All rights reserved. |
| PdfPig | 0.1.16 | Apache-2.0 (nuspec license expression; the package has no license file) | Authors: UglyToad. The nuspec has no copyright element. |
| CommunityToolkit.Mvvm | 8.3.2 | MIT | (c) .NET Foundation and Contributors. All rights reserved. |
| System.Drawing.Common | 10.0.0 | MIT | © Microsoft Corporation. All rights reserved. |
| Microsoft.Win32.SystemEvents | 10.0.0 | MIT | © Microsoft Corporation. All rights reserved. |
| System.Security.Cryptography.ProtectedData | 10.0.0 | MIT | © Microsoft Corporation. All rights reserved. |

`Microsoft.Windows.SDK.NET.dll` and `WinRT.Runtime.dll` are Microsoft files that the .NET SDK adds for Windows 10/11 targets
(the C#/WinRT projection, MIT license, https://github.com/microsoft/CsWinRT). The .NET runtime itself is Microsoft software
under the MIT license (https://github.com/dotnet/runtime/blob/main/LICENSE.TXT).

## MIT license text

The following text is `LICENSE.TXT` from System.Drawing.Common 10.0.0 and System.Security.Cryptography.ProtectedData 10.0.0. System.IO.Packaging 8.0.1 and Microsoft.Win32.SystemEvents 10.0.0 declare the same MIT expression in their nuspec files. DocumentFormat.OpenXml and DocumentFormat.OpenXml.Framework declare MIT in the nuspec and do not include a license file. `System.Text.Encoding.CodePages` is part of the .NET 10 shared framework, so it is no longer a separate package.

```
The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

CommunityToolkit.Mvvm 8.3.2 ships `License.md`. Its copyright line is "Copyright © .NET Foundation and Contributors". The permission text is the MIT license above.

## PdfPig

PdfPig 0.1.16 `pdfpig.nuspec` says `<license type="expression">Apache-2.0</license>` and `<authors>UglyToad</authors>`. The package does not contain a license file. The Apache License 2.0 text is in `LICENSE` at the root of this repository.
