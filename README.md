# Live Sheet Jira Board (Full-Stack .NET 8 MVC + Bootstrap 5 + GLightbox)

An interactive, high-density **Jira Kanban Board, Table & Photo Gallery Viewer** built with ASP.NET Core 8 MVC and Bootstrap 5, connected directly to your live **SharePoint / Excel Online or Google Sheet workbook**. Strictly **view-read only**, highly compact, responsive, and equipped with **GLightbox** for high-resolution screenshot inspection.

---

## ✨ Key Features

- 🔒 **Strict View-Read Only Architecture**:
  - Pure view & read mode with zero mutation risk or accidental moves.
  - Drag-and-drop and in-app edits completely disabled to keep live data 100% intact.
  - Prominent "View Only" indicator.
- ⚡ **Ultra-Compact, High-Density UI**:
  - Designed for rapid scanning across **544+ live issues**.
  - **Density Toggle**: Switch seamlessly between **Compact** (tight rows, 2-line title clamp, 68px preview thumbnails) and **Comfortable** views. Choice is remembered in `localStorage`.
- 🖼️ **3rd-Party GLightbox HD Screenshot Viewer**:
  - Zero-dependency responsive lightbox with full pinch-to-zoom, pan, and slide navigation across bug screenshots.
  - Embedded screenshots extracted directly from the OpenXML layer (`/media/image*.png`).
  - High-res fullscreen view with hover indicators and thumbnail carousel.
- ⌨️ **Productivity Keyboard Shortcuts**:
  - Press <kbd>/</kbd> anywhere to instantly focus and select the live search bar.
  - Press <kbd>1</kbd> to jump to **Board** view.
  - Press <kbd>2</kbd> to jump to **Table** view.
  - Press <kbd>3</kbd> to jump to **Gallery** view.
  - Press <kbd>Esc</kbd> to dismiss search focus or close modals.
- 📋 **One-Click Issue Key Copy**:
  - "Copy Key" button inside the detail modal copies the issue key (e.g. `BUG-337`) to clipboard with visual confirmation and toast notification.
- 🧪 **QA Reproduction & Verification Panels**:
  - Structured view for **Steps to Reproduce**, **Actual Result** (highlighted in soft red), and **Expected Result** (highlighted in soft green).
  - Comments and dynamic Excel sheet attributes displayed cleanly without clutter.
- 🔍 **Instant Live Search & Multi-Select Filters**:
  - Filter by multiple statuses, multiple assignees/developers, and multiple priorities simultaneously.
  - Real-time client-side instant filtering with zero server roundtrip lag.
  - One-click active filter chips (`✕`) and **Clear All** button.
  - **Photos Only** filter toggle.
- 📑 **Switchable Views**:
  - **Kanban Board**: Jira-style columns (`Tested and Closed`, `Open`, `Ready for QA`, `Reopen`, `Merged`).
  - **Table / Spreadsheet**: Compact, Excel-style sortable columns (Key, Type, Title, Status, Priority, Assignee).
  - **Photo Gallery**: High-density screenshot grid for visual QA triage.
- 🟢 **Live Cloud Sync & Excel Online Integration**:
  - One-click **"Excel Online"** button opens your live cloud spreadsheet directly in Microsoft Excel Online.
  - One-click **"Sync Live Sheet"** button updates the board with fresh data from the cloud sheet.
  - One-click **"Export CSV"** button downloads the full dataset.

---

## 🚀 How to Run

In this folder (`d:\RingNet\JIRA Sample`):

1. Double-click **`run.bat`** (or execute `dotnet run --launch-profile http`).
2. Open your browser at:
   👉 **`http://localhost:5057`**

---

## 📊 Live Columns Mapped

| Column Name | Mapped Jira Field |
| --- | --- |
| `Bug no.` | Issue Key (`BUG-1`, `BUG-2`, etc.) |
| `Bug - Title` | Card Summary / Title |
| `Description` | Full Issue Description |
| `Screenshots` | Embedded Screenshots (`/media/image*.png`) |
| `Critical` | Jira Priority (`Critical`, `Major`, `Minor`) |
| `QA Status` | Board Column (`Tested and Closed`, `Open`, `Ready for QA`, `Reopen`) |
| `Developer` | Assignee |
| `Module` & `Feature/Module` | Tags & Component Badges |
| `Steps to Reproduce` | QA Reproduction Panel |
| `Actual Result` | QA Actual Result Panel |
| `Expected Result` | QA Expected Result Panel |
| `Created Date` | Issue Creation Date |
| `Comments` / `Dev Status` | Detail Modal Custom Fields |
