# Privacy Policy

**Claude Usage Dock** (the "extension")

_Last updated: 2026-07-14_

Claude Usage Dock is a PowerToys Command Palette extension that displays your
own Claude Code subscription usage. This policy describes exactly what data the
extension touches. It reflects what the source code does — the project is open
source, so every claim here can be checked against the code.

**In one sentence:** the extension reads the Claude Code credentials already
stored on your PC and uses them to ask Anthropic for your usage figures; it
sends nothing to the developer or any third party, and shows no ads.

## What the extension accesses

- **Your local Claude Code credentials.** The extension reads the OAuth access
  token and refresh token that Claude Code stores on your computer, plus your
  plan name (e.g. "Pro"). These are read from your local credentials file (or,
  for additional accounts, from a file path you configure yourself). They are
  used only to authenticate the usage requests described below.

The extension does **not** read, collect, or store your name, email address,
contact list, location, browsing history, files, or any advertising identifier.

## What the extension transmits, and to whom

The extension communicates **only with Anthropic's official servers** — the
same servers Claude Code itself uses. Specifically:

- Your access token is sent to `https://api.anthropic.com` to retrieve your
  usage figures (how much of your session and weekly limits remain).
- Your refresh token is sent to `https://console.anthropic.com` only when the
  access token has expired, to obtain a new one — the same token refresh Claude
  Code performs.

Your data is **never** sent to the extension's developer, and **never** sent to
any third party, analytics service, or advertising network. The extension
contains no telemetry.

Your use of Anthropic's services is governed by
[Anthropic's Privacy Policy](https://www.anthropic.com/legal/privacy).

## What the extension stores on your device

- **A local usage history file** (`usage-history.csv`) in the extension's
  per-user application-data folder. Each line contains only a timestamp and two
  numbers — the percentage of your session and weekly limits remaining at that
  moment. It contains **no** tokens, account identifiers, or other personal
  information. It is used to draw the burn-rate estimate, breakdown statistics,
  and heatmap, and it is kept for about 36 days before old entries are pruned.
- **Your settings** (refresh interval, alert threshold, notification toggle,
  and any additional-account labels and file paths you enter), stored in the
  Command Palette settings file on your device.
- **Refreshed credentials.** When the extension renews an expired token, it
  writes the new token back into your local Claude Code credentials file so
  Claude Code keeps working. This stays on your device.

All of this data stays on your computer. None of it is uploaded anywhere.

## Data sharing and selling

The extension does not share or sell any data. It has no server of its own and
collects nothing from you.

## Removing your data

Uninstalling the extension removes its local application data, including the
usage history file and settings. Your Claude Code credentials file belongs to
Claude Code and is left in place.

## Children's privacy

The extension is a developer tool and is not directed at children. It collects
no personal information for anyone to gather in the first place.

## Changes to this policy

If the data practices described here change, this document will be updated and
the "Last updated" date above revised. Material changes will be noted in
[CHANGELOG.md](CHANGELOG.md).

## Contact

Questions about this policy can be raised as an issue on the project's
repository:
<https://github.com/shaikh-rashid/claude-command-palette-dock>
