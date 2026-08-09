# Third-party notices

RcktMon itself is distributed under the MIT License (see [LICENSE](LICENSE)).

This file records third-party code that has been **vendored into this repository**
(copied into the source tree rather than consumed as a package). Ordinary NuGet and
npm dependencies are not listed here — they are declared in
`Directory.Packages.props` and in the respective `package.json` files, and are
distributed under their own licenses by their own publishers.

## Current

_None._ As of the Avalonia / T-Invest rewrite, this repository contains no vendored
third-party source. The entries below are retained for the historical record.

## Historical

Both entries below were present in the source tree from 2020 until the rewrite, and
both were shipped **without the license text their licenses require**. The code has
since been removed; these notices are provided to remedy the omission for the
historical commits, which remain reachable via the `legacy/wpf` branch and the
`v1.6.6-wpf-final` tag.

### Tinkoff Invest .NET SDK (`Tinkoff.Trading.OpenApi.Legacy/`)

A vendored fork of the official Tinkoff Invest OpenAPI C# SDK, version 1.7.5,
retargeted to .NET 6.

- Upstream: <https://github.com/TinkoffCreditSystems/invest-openapi-csharp-sdk>
- Author: Nikita Kamensky · Company: Tinkoff
- License: **Apache License 2.0**

Apache-2.0 §4 requires that redistribution include a copy of the license and any
`NOTICE` file. The full license text is reproduced in
[`third-party/Apache-2.0.txt`](third-party/Apache-2.0.txt).

Removed during the rewrite; the current client does not talk to any broker API, and
the server uses the official [`Tinkoff.InvestApi`](https://www.nuget.org/packages/Tinkoff.InvestApi)
package as a normal NuGet dependency.

### ConcurrentHashSet (`CoreNgine/Platform/ConcurrentHashSet.cs`)

A single-file copy of i3arnon's `ConcurrentHashSet`, declared in the
`System.Collections.Concurrent` namespace. The file carried only a source-URL comment.

- Upstream: <https://github.com/i3arnon/ConcurrentHashSet>
- Copyright (c) Bar Arnon
- License: **MIT**

The full license text is reproduced in [`third-party/MIT-i3arnon.txt`](third-party/MIT-i3arnon.txt).

Removed during the rewrite.

---

> This file is a good-faith attribution record, not legal advice. If you need
> certainty about the historical distribution, consult a lawyer.
