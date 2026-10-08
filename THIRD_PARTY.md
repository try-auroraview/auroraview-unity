# Third-party notices

`Editor/WebAssets/vendor/event_bridge.js` is copied unchanged from
[`try-auroraview/auroraview`](https://github.com/try-auroraview/auroraview),
`crates/auroraview-core/src/assets/js/core/event_bridge.js`, under the MIT license
included in this repository. Generated upstream by `@auroraview/sdk`.

SHA256: `4550b027e400c6500fca5e64dd8c50348bde15848c513d2738dd6e443b4a17ff`.
The bridge test enforces this digest; update the notice and interoperability tests
together when importing a new upstream revision.

Microsoft.Web.WebView2 1.0.3537.50 is downloaded from Microsoft's official NuGet
package feed at build time. Its publisher catalog SHA512 is verified before use;
if the official catalog leaf is unavailable, a trusted Microsoft author signature
is required through `dotnet nuget verify --all` instead.
It is licensed under Microsoft's WebView2 SDK terms, contained in the package's
`LICENSE.txt`. The release package includes those terms. Microsoft Edge WebView2
Runtime is installed and updated independently by Microsoft; it is not bundled.

Unity Editor is a separately licensed prerequisite and is not redistributed.
