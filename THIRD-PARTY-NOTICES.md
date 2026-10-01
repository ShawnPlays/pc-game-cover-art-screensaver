# Third-party notices

PC Game Cover Art Screensaver is released under the [MIT License](LICENSE). It also includes, or is built with, the
following third-party software.

## Included in `PCGameCoverArt.scr`

The screensaver is published as one self-contained file, so it includes the .NET runtime and Windows Presentation
Foundation (WPF). Users don't need to install .NET separately.

| Component | License | Source |
|---|---|---|
| .NET Runtime | MIT | https://github.com/dotnet/runtime |
| Windows Desktop Runtime (WPF) | MIT | https://github.com/dotnet/wpf |

Both are covered by this license:

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

The .NET runtime itself includes components from other projects. Their notices are listed in the .NET projects'
own third-party notices:

- https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT
- https://github.com/dotnet/wpf/blob/main/THIRD-PARTY-NOTICES.TXT

The music is played with [NAudio](https://github.com/naudio/NAudio) (the `NAudio.Core` and `NAudio.Wasapi` packages),
under this license:

```
MIT License

Copyright (c) Mark Heath

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

## Used to build, but not included

These are needed to build the project but aren't part of the files users download.

| Component | License | Used for |
|---|---|---|
| [Playnite SDK](https://github.com/JosefNemec/Playnite) | MIT, Copyright © Josef Nemec | Building the Playnite add-on. Playnite provides its own copy at run time, so the add-on package doesn't include it. |
| [xUnit.net](https://github.com/xunit/xunit) | Apache-2.0 | Unit tests |
| [Microsoft.NET.Test.Sdk](https://github.com/microsoft/vstest) | MIT | Unit tests |
| [Microsoft.NETFramework.ReferenceAssemblies](https://github.com/microsoft/dotnet) | MIT | Building the add-on without the .NET Framework developer pack |

## Steam data

The Steam option reads files that Steam keeps on your PC. The list of Steam store tag names built into the program
(`src/CoverArtSaver.Core/Steam/SteamTags.cs`) comes from Steam's public tag list. Steam and the Steam logo are
trademarks of Valve Corporation. This project isn't affiliated with or endorsed by Valve.

## Game cover art

The screensaver displays cover art from your own Playnite or Steam library. That artwork belongs to its respective owners.
This project doesn't include or distribute any.
