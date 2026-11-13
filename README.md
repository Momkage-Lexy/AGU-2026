# AGU 2026 Booth Survey

A full-screen survey kiosk for the Oregon State University booth at AGU 2026. Visitors answer a short two-round survey, then spin a prize wheel. Booth staff can manage the prizes from a PIN-protected admin screen.

The app is a Windows desktop program (WinForms on .NET 8) that shows the survey, an HTML page, inside a WebView2 browser control. Responses and prize stock are saved to a local SQLite database, and a CSV copy is kept up to date for Excel.

---

## Contents

- [Features](#features)
- [Requirements](#requirements)
- [Project layout](#project-layout)
- [Build and run on your PC](#build-and-run-on-your-pc)
- [Setting up the tablet](#setting-up-the-tablet)
- [Using the admin screen](#using-the-admin-screen)
- [Where the data goes](#where-the-data-goes)
- [Changing the survey](#changing-the-survey)

---

## Features

- **Two-round survey**: role and impression questions, then what brought the visitor to the booth, which OSU research centers they already knew about, and an optional follow-up contact form.
- **Prize wheel**: one slice per prize in stock. When a prize runs out, its slice disappears. If every prize is gone, visitors see a thank-you message instead.
- **Admin screen** (PIN protected): add, rename, restock and remove prizes, see how many of each have been given away, change the PIN, and close the app.
- **Kiosk mode**: full screen with no title bar, always on top. Visitors can't close it (Alt+F4 is blocked), zoom, open menus or browse away.
- **Automatic saving**: every response goes into `survey.db` and is written to `survey_export.csv` immediately.

---

## Requirements

| What | Notes |
|---|---|
| Windows 10 or 11 | The app is Windows-only. |
| [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) | Needed to build. Not needed on the tablet if you publish a self-contained build. Check what you have with `dotnet --list-sdks`. |
| Microsoft Edge WebView2 Runtime | Comes with Windows 10 and 11. If the survey shows a blank screen, install it from Microsoft. |

NuGet packages (restored automatically by `dotnet restore`):

- `Microsoft.Web.WebView2`
- `Microsoft.Data.Sqlite`: use **version 10.0.0** (or 8.0.x). Version 11 and later require .NET 10 and won't install.

---

## Project layout

```
AGU-2026/
├── .gitignore
├── README.md
└── windows/                 # the desktop app
    ├── windows.csproj
    ├── Program.cs
    ├── Form1.cs             # window, kiosk mode, database, prize + admin logic
    ├── Form1.Designer.cs    # the WebView2 control on the form
    └── wwwroot/
        ├── index.html       # the survey, prize wheel and admin screen
        ├── assets/                  # images and fonts used by the survey page
        ├── images/              # logo, answer icons, research center tiles
        └── fonts/               # Stratum2 fonts
```

The page loads images from `assets/images/` and fonts from `assets/fonts/`, relative to `wwwroot/index.html`. The project file copies `wwwroot` into the build output so those paths still work after building.

### Important settings in `windows.csproj`

```xml
<PropertyGroup>
  <OutputType>WinExe</OutputType>
  <TargetFramework>net8.0-windows</TargetFramework>
  <Nullable>enable</Nullable>
  <UseWindowsForms>true</UseWindowsForms>
  <UseWPF>true</UseWPF>          <!-- silences a WindowsBase warning from WebView2; the app is still WinForms -->
  <ImplicitUsings>enable</ImplicitUsings>
</PropertyGroup>

<ItemGroup>
  <!-- The survey page -->
  <Content Include="wwwroot\**\*.*">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

---

## Build and run on your PC

From the `windows` folder:

```powershell
dotnet restore
dotnet build
dotnet run
```

The app opens full screen. To close it while developing, either:

- open the admin screen and tap **Close app** twice, or
- press **Ctrl+C** in the terminal where `dotnet run` is running.

### Trying the page without the app

You can open `wwwroot/index.html` directly in Chrome or Edge to check layout and flow. It runs in a **test mode**: the prize wheel and admin screen work (PIN `1234`), but nothing is saved.

---

## Setting up the tablet

### Option A: build on the tablet

1. Install Git and the .NET 8 SDK on the tablet.
2. Clone the repository.
3. From the repo folder run:
   ```powershell
   dotnet run --project windows -c Release
   ```

### Option B: copy a ready-made build (no .NET install needed)

On your PC, from the repo folder:

```powershell
# Most tablets (Intel / AMD)
dotnet publish windows -c Release -r win-x64 --self-contained true

# ARM tablets (Surface Pro X, Snapdragon models)
dotnet publish windows -c Release -r win-arm64 --self-contained true
```

Copy the whole folder it prints (`windows\bin\Release\net8.0-windows\<runtime>\publish\`) to the tablet and run `windows.exe`. Keep the `wwwroot` folder next to the `.exe`.

### Before the conference

- [ ] **Change the admin PIN** from the default `1234` (admin screen → *Change admin PIN*).
- [ ] Set the prize names and counts on the admin screen.
- [ ] Do one complete test survey, answering **Yes** to the follow-up question, and check that `survey_export.csv` contains the row.
- [ ] Optionally delete `survey.db` and `survey_export.csv` after testing to start the event with an empty response list. This also resets the prizes and PIN to the defaults in `Form1.cs`, so set them again afterward.

### Locking the tablet down further

The app blocks its own close button, Alt+F4, zoom, menus and browsing. It **can't** block Windows-level actions such as edge swipes, the Windows key, Alt+Tab or Ctrl+Alt+Del. For a fully locked device, use Windows' kiosk features (Assigned Access, or Shell Launcher on Enterprise/Education editions). Which option is available depends on the tablet's Windows edition.

---

## Using the admin screen

**To open it:** press and hold the OSU logo in the top-left corner for **3 seconds**, or press **Ctrl+Shift+A** on a keyboard. Enter the PIN.

A normal tap on the logo resets the survey to the first question.

| Task | How |
|---|---|
| Restock or correct a count | Change the number in **Left**. It saves when you leave the box. |
| Rename a prize | Edit the name. It saves when you leave the box. |
| Add a prize | Type a name and how many, then tap **Add**. |
| Remove a prize | Tap **Remove**, then tap again within 3 seconds to confirm. |
| Change the PIN | Open *Change admin PIN*, enter 4–8 digits, tap **Save PIN**. |
| Close the app | Tap **Close app**, then tap again within 3 seconds. |
| Leave the admin screen | Tap **Done** or press Esc. |

**How the wheel works**

- Each prize with at least 1 left gets one equal slice, so every prize on the wheel has the same chance of winning.
- One is taken off a prize's count as soon as the wheel stops on it.
- A prize at 0 is hidden from the wheel but stays in the admin list (marked *Out of stock*) so it can be restocked.
- The first time the app runs, it adds Beach Ball, Giant Highlighter, Calendar, Sticker and Magnet with 25 of each. These defaults live at the top of `Form1.cs` (`DefaultPrizes`, `DefaultPrizeQuantity`, `DefaultAdminPin`) and are only used when `survey.db` is first created.

---

## Where the data goes

All files are created next to the `.exe` (for `dotnet run`, that's `windows\bin\Debug\net8.0-windows\`):

| File | What's in it |
|---|---|
| `survey.db` | SQLite database: `responses` (survey answers), `prizes` (stock), `settings` (PIN). |
| `survey_export.csv` | Every response, rewritten after each survey. Opens in Excel. |
| `errors.log` | Only appears if something goes wrong saving data. |

**Columns saved for each response:** submitted at, role, experience, what brought them, centers known (separated by `; `), willing to follow up, first name, last name, email, date of birth, planned grad school entry, academic program, prize won.

> **Privacy:** these files contain visitors' names, emails and birth dates. They're listed in `.gitignore` so they're never committed. After the event, copy `survey_export.csv` off the tablet with a USB drive or OneDrive, not through GitHub, and delete it from the tablet once it's stored safely.

Updating the app keeps existing data. If a new version adds survey columns, they're added to the existing database automatically, and older rows show those columns as blank.

---

## Changing the survey

Questions live in `wwwroot/index.html` (`round1Questions` and `round2Questions`). Answer icons are loaded from `assets/images/`, named after the answer text in lowercase with spaces replaced by dashes (for example, *Graduate Student* → `graduate-student.png`).

**To save a new field to the database:**

1. In `index.html`, include the value in the `sendToHost({ type: "survey", ... })` call in the `closeModal` click handler.
2. In `Form1.cs`:
   - add the column and CSV heading to the `Fields` list,
   - add a matching property to `SurveyResult` (same name as the key sent from the page),
   - add one line for it in `GetValue()`.
3. Rebuild. The new column is added to the existing database on the next launch.

### How the page and the app talk

The page sends messages with `sendToHost({ type: ... })`, and `Form1.cs` handles them in `CoreWebView2_WebMessageReceived`.

| Message from the page | What the app does |
|---|---|
| `survey` | Saves the response and rewrites the CSV. |
| `getPrizes` | Replies with `prizes` (the full prize list). |
| `prizeWon` | Takes one off that prize, replies with `prizes`. |
| `adminLogin` | Checks the PIN, replies with `adminLogin { ok }`. |
| `adminLogout` | Locks admin actions again. |
| `savePrize`, `deletePrize` | Admin only. Changes the prize list, replies with `prizes`. |
| `changePin` | Admin only. Replies with `pinChanged`. |
| `closeApp` | Admin only. Closes the app. |

Admin-only messages are refused by the app unless the correct PIN has been entered, even if someone triggers them from the page.

---

