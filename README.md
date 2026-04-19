# HelpCenterUI

**Oxide/uMod Plugin for Rust** — In-game help center with categories, subcategories, paged content and an admin editing interface.

![Version](https://img.shields.io/badge/version-1.5.2-blue?style=flat-square)
![Rust](https://img.shields.io/badge/game-Rust-orange?style=flat-square)
![Oxide](https://img.shields.io/badge/framework-Oxide%2FuMod-green?style=flat-square)
[![Discord](https://img.shields.io/badge/Discord-GameZoneOne-5865F2?style=flat-square&logo=discord&logoColor=white)](https://discord.gg/dx2q8wNM9U)

---

## Screenshots

> Replace with actual screenshots — upload to `screenshots/` in the repo and update the paths below.

| Main menu | Category page |
|---|---|
| ![Main menu](https://placehold.co/480x270/0d1117/4d9375?text=Main+Menu) | ![Category page](https://placehold.co/480x270/0d1117/4d9375?text=Category+Page) |

---

## Features

- Full **CUI-based help center** — opens as an in-game UI overlay
- Up to **5 main categories**, each with up to **12 subcategories per page**
- **Paged content** — long articles split into multiple pages automatically
- **Admin editing** — edit categories and pages directly in-game without touching files
- **Auto command index** — automatically lists all registered server commands
- **Team roster** page — shows current server team/staff
- Optional **background image** via ImageLibrary
- Configurable chat commands per category

## Dependencies

| Plugin | Required | Notes |
|---|---|---|
| [ImageLibrary](https://umod.org/plugins/image-library) | Optional | Custom background image support |

## Installation

1. Copy `HelpCenterUI.cs` into your `oxide/plugins/` folder
2. *(Optional)* Install ImageLibrary for background image support
3. Open the help center in-game and use the admin edit mode to fill in your content

## Permissions

| Permission | Description |
|---|---|
| `helpcenterui.admin` | Full admin access |
| `helpcenterui.edit` | Can edit content without full admin rights |

## Usage

- Open with the configured chat command (default: `/help`)
- Navigate categories and subcategories via the UI buttons
- **Admins**: click the edit button to modify content in-game

## Author

Made by **[GameZoneOne](https://discord.gg/dx2q8wNM9U)**  
📧 info@gamezoneone.de
