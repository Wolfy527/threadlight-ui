# Shared UI boundary

Keep reusable styling, branding, controls, layout helpers, animation, tooltips and presentation preferences here. A helper may be shared even when only creator tools currently use it. Installing an unused helper must not expose creator functionality.

Actual customer inspectors belong in Components. Creator screens, workflow logic, generation actions and product-specific integrations belong in Authoring or their Builder. Shared UI must not reference those packages or the VRChat SDK.

Controls can attach callbacks for their active visual elements and must release them when detached. Do not register product windows, inspectors or automatic startup work here. The shared Reduced Motion menu is an accessibility preference for all users and belongs here.

Run `.github/scripts/validate-package.ps1 -PackageRoot . -ExpectedName com.wolfyvr.threadlight.ui` before publishing. CI checks the boundary on pushes, pull requests and releases. Source checks are a guard against common mistakes; review remains necessary for indirect or dynamic integrations.
