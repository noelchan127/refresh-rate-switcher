# Security Policy

## Supported versions

Only the [latest release](https://github.com/noelchan127/refresh-rate-switcher/releases/latest)
receives security fixes. Please update to it before reporting a problem.

## Reporting a vulnerability

**Please don't report security problems publicly.** This repository has no public issue tracker.

Report them privately through GitHub instead:

1. Go to the [Security tab](https://github.com/noelchan127/refresh-rate-switcher/security) of this repository.
2. Click **Report a vulnerability**
   ([direct link](https://github.com/noelchan127/refresh-rate-switcher/security/advisories/new)).
3. Describe the problem, the version and Windows version you used, and the steps to reproduce it.

Only the maintainer can see the report. This is a one-person hobby project, so replies are
best-effort, but I'll let you know what happens next. Once a fix is released, you'll be credited in the advisory unless you'd prefer not to be.

## Scope

In scope:
- The Refresh Rate Switcher app and its installer/uninstaller
- The build and release workflow in `.github/workflows`

Not security issues (you're still welcome to report them, and ordinary bugs, through the same private form):
- The Windows SmartScreen / antivirus warning caused by the installer not being code-signed
- Incompatibility with Windows 11 Dynamic refresh rate (DRR), described in the README
