# Third-party notices

Code, data and fonts adapted from or bundled with this project, with the
notices their licenses require. NuGet packages are not listed here; their
licenses ship with the packages.

## Noto Sans Bengali

Used for: Bangla text in generated PDF reports. The Regular and Bold TrueType
files are embedded in `SmartPlagiarism.Infrastructure` from
`src/SmartPlagiarism.Infrastructure/Reports/Fonts/`, where the full SIL Open
Font License 1.1 is kept alongside them (`OFL.txt`).

Also used for: Bangla text on the web pages, as a variable WOFF2 file in
`src/SmartPlagiarism.Web/wwwroot/fonts/`, with its license alongside
(`LICENSE-noto-sans-bengali.txt`). From the `@fontsource-variable/noto-sans-bengali`
5.3.0 package.

Source: https://github.com/notofonts/bengali -
Copyright 2022 and 2025 The Noto Project Authors.

## Inter

Used for: the text of the web pages, as a variable Latin WOFF2 file in
`src/SmartPlagiarism.Web/wwwroot/fonts/`, under the SIL Open Font License 1.1
kept alongside it (`LICENSE-inter.txt`). From the
`@fontsource-variable/inter` 5.3.0 package.

Source: https://github.com/rsms/inter -
Copyright 2016 The Inter Project Authors.

## Source Serif 4

Used for: headings on the student's result page and in generated PDF reports.

- Web: a variable Latin WOFF2 file in `src/SmartPlagiarism.Web/wwwroot/fonts/`,
  with its license alongside (`LICENSE-source-serif-4.txt`). From the
  `@fontsource-variable/source-serif-4` 5.3.1 package.
- PDF: `SourceSerif4-Semibold.ttf`, embedded in `SmartPlagiarism.Infrastructure`
  from `src/SmartPlagiarism.Infrastructure/Reports/Fonts/`, unmodified from release
  4.005R, with its license alongside (`OFL-SourceSerif4.txt`).

Both are under the SIL Open Font License 1.1.

Source: https://github.com/adobe-fonts/source-serif -
Copyright 2014 - 2023 Adobe (http://www.adobe.com/), with Reserved Font Name 'Source'.

## bijoy-to-unicode-file-converter 1.2

Used for: the Bijoy (SutonnyMJ) glyph table in
`src/SmartPlagiarism.Engine/Text/BijoyConverter.cs`. The reordering logic in
that file is this project's own.

Source: https://github.com/almehady/Bijoy-to-Unicode-File-Converter
(PyPI: `bijoy-to-unicode-file-converter`)

```
MIT License

Copyright (c) [2021] [Al Mehady]

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
