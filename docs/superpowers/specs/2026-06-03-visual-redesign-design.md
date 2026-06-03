# Visual Redesign — AIHelperNET

**Date:** 2026-06-03
**Branch:** develop
**Status:** Approved, ready for implementation

## Goal

Bring the entire application into a consistent, modern visual style. Replace scattered hardcoded colors and ad-hoc font sizes with a token-driven design system. Add proper interactive states (hover, pressed, disabled) to all controls. Introduce runtime theme switching between dark navy and light slate.

## Scope

- `src/AIHelperNET.App/App.xaml` — entry point; merges resource dictionaries
- `src/AIHelperNET.App/Resources/DarkTheme.xaml` — new file; dark color tokens
- `src/AIHelperNET.App/Resources/LightTheme.xaml` — new file; light color tokens
- `src/AIHelperNET.App/Resources/Sizes.xaml` — new file; typography + spacing tokens
- `src/AIHelperNET.App/Resources/Styles.xaml` — new file; all control styles
- `src/AIHelperNET.App/Windows/MainOverlayWindow.xaml` — update to use tokens + new controls
- `src/AIHelperNET.App/Windows/MainOverlayWindow.xaml.cs` — add `ToggleTheme_Click`
- `src/AIHelperNET.App/Windows/SettingsWindow.xaml` — apply dark/light tokens
- `src/AIHelperNET.App/App.xaml.cs` — add `ThemeManager` static class

Out of scope: animations beyond simple trigger-based transitions, new features, layout restructuring.

---

## Architecture: Token-First ResourceDictionary

`App.xaml` merges four resource dictionaries in this order:

1. `DarkTheme.xaml` (default; swapped at runtime for `LightTheme.xaml`)
2. `Sizes.xaml`
3. `Styles.xaml`
4. Converter instances (currently inline in App.xaml, stay there)

All control styles in `Styles.xaml` use `DynamicResource` exclusively — never hardcoded color values. This is what makes runtime theme switching work: when `ThemeManager` replaces the theme dictionary, WPF re-resolves every `DynamicResource` binding automatically.

---

## Color Tokens

Both theme files define the same set of keys. Values differ per theme.

| Token key | Dark value | Light value |
|---|---|---|
| `Brush.Background.Window` | `#1A1A2E` | `#F1F5F9` |
| `Brush.Background.Panel` | `#0D0D1A` | `#E8EDF3` |
| `Brush.Background.Sidebar` | `#0A0A1A` | `#DDE3EC` |
| `Brush.Background.Card` | `#111827` | `#FFFFFF` |
| `Brush.Background.TitleBar` | `#0D0D1A` | `#E2E8F0` |
| `Brush.Background.Button` | `#2A2A4A` | `#CBD5E1` |
| `Brush.Background.Button.Hover` | `#3A3A6A` | `#B8C5D6` |
| `Brush.Background.Button.Pressed` | `#1E1E38` | `#A0B0C4` |
| `Brush.Foreground.Primary` | `#EAEAEA` | `#1E293B` |
| `Brush.Foreground.Secondary` | `#AAAAAA` | `#475569` |
| `Brush.Foreground.Muted` | `#666666` | `#94A3B8` |
| `Brush.Foreground.Button` | `#CCCCCC` | `#1E293B` |
| `Brush.Accent` | `#4F8EF7` | `#4F8EF7` |
| `Brush.Semantic.Active` | `#44FF88` | `#16A34A` |
| `Brush.Semantic.Question` | `#FFCC44` | `#D97706` |
| `Brush.Border` | `#2A2A3A` | `#CBD5E1` |
| `Brush.Splitter` | `#2A2A4A` | `#94A3B8` |
| `Brush.Splitter.Hover` | `#804F8EF7` | `#804F8EF7` |

---

## Typography & Spacing Tokens (`Sizes.xaml`)

These are theme-independent `sys:Double` and `Thickness` resources.

| Token | Value | Usage |
|---|---|---|
| `Font.XS` | `9` | Section labels, timestamps, status line |
| `Font.SM` | `11` | UI text, buttons, sidebar, transcript rows |
| `Font.MD` | `12` | Answer body (monospace) |
| `Spacing.XS` | `2` | Tight item gaps |
| `Spacing.SM` | `4` | Button padding, small gaps |
| `Spacing.MD` | `8` | Panel padding, card padding |
| `Spacing.LG` | `12` | Section gaps |
| `Spacing.XL` | `16` | Window margin (SettingsWindow) |

---

## Control Styles (`Styles.xaml`)

### Button (default)

- `Background="{DynamicResource Brush.Background.Button}"`
- `Foreground="{DynamicResource Brush.Foreground.Button}"`
- `BorderThickness=0`, `CornerRadius=3`, `Padding=6,3`
- `FontSize="{DynamicResource Font.SM}"`
- `ControlTemplate` with triggers:
  - `IsMouseOver=true` → `Background="{DynamicResource Brush.Background.Button.Hover}"`
  - `IsPressed=true` → `Background="{DynamicResource Brush.Background.Button.Pressed}"`
  - `IsEnabled=false` → `Opacity=0.4`

### ActionBtn (key: `ActionBtn`)

Extends the default Button style. Overrides: `Padding=5,2`, `Margin=0,0,4,0`. All interactive triggers inherited.

### RadioButton (key: `RadioBtn`)

Custom `ControlTemplate`:
- A horizontal `StackPanel` containing an 8×8 `Ellipse` (the indicator) and a `ContentPresenter`
- Unchecked: ellipse `Fill=Transparent`, `Stroke="{DynamicResource Brush.Foreground.Muted}"`, `StrokeThickness=1.5`
- Checked: ellipse `Fill="{DynamicResource Brush.Accent}"` with a 4px white inner dot (nested `Ellipse`)
- `Foreground` defaults to `Brush.Foreground.Secondary`; `IsChecked=true` trigger sets it to `Brush.Foreground.Primary`
- `IsMouseOver=true` trigger sets `Foreground=Brush.Foreground.Primary`
- `FontSize="{DynamicResource Font.SM}"`

### SectionLabel (key: `SectionLabel`, TargetType=TextBlock)

- `Foreground="{DynamicResource Brush.Foreground.Muted}"`
- `FontSize="{DynamicResource Font.XS}"`
- `FontWeight=SemiBold`
- `Margin=0,0,0,4` (bottom margin only; callers set top margin inline per their context)

### GridSplitter

- `Background="{DynamicResource Brush.Splitter}"`
- `IsMouseOver=true` trigger: `Background="{DynamicResource Brush.Splitter.Hover}"`

### ScrollBar (custom template)

- Track: `Background=Transparent`
- Thumb: 6px wide, `CornerRadius=3`, `Fill="{DynamicResource Brush.Foreground.Muted}"`, `Opacity=0.5`
- `IsMouseOver=true` on thumb: `Opacity=0.8`
- Repeat buttons (arrows): `Visibility=Collapsed`
- Applied via `ScrollViewer` style that sets the `VerticalScrollBarStyle`

---

## Window Updates

### MainOverlayWindow.xaml

- `Background="{DynamicResource Brush.Background.Window}"`
- Remove all inline `Background`, `Foreground`, `FontSize` hardcodes; replace with `DynamicResource` token references
- Title bar `Border`: `Background="{DynamicResource Brush.Background.TitleBar}"`
- Sidebar `Border`: `Background="{DynamicResource Brush.Background.Sidebar}"`, `BorderBrush="{DynamicResource Brush.Border}"`
- Transcript `Border`: `Background="{DynamicResource Brush.Background.Panel}"`
- Answer `Border`: `Background="{DynamicResource Brush.Background.Panel}"`
- Turn card `Border`: `Background="{DynamicResource Brush.Background.Card}"`, `BorderBrush="{DynamicResource Brush.Border}"`, `BorderThickness=1`
- Turn card gets a left accent strip: a `Rectangle` with `Width=3`, `Fill="{DynamicResource Brush.Accent}"` docked left inside the card's outer `DockPanel`
- `GridSplitter`: uses new style (no inline `Background`)
- Add theme toggle button in title bar: `Content="◐"`, `Click="ToggleTheme_Click"`, uses `tb-icon` pattern (transparent background, `FontSize=13`)
- All `TextBlock` section labels (`TRANSCRIPT`, `MODE`, `AUDIO SOURCE`, `STATUS`): apply `SectionLabel` style
- `RadioButton` elements: apply `RadioBtn` style
- Existing `RadioBtn` and `StatusDot` local styles in `Window.Resources`: remove (replaced by App-level styles)

### SettingsWindow.xaml

Full redesign to match the active theme:
- `Background="{DynamicResource Brush.Background.Window}"`
- Outer container padding increased to `Margin=20`
- "Claude API Key" label: apply `FontSize="{DynamicResource Font.SM}"`, `FontWeight=SemiBold`, `Foreground="{DynamicResource Brush.Foreground.Primary}"`
- `PasswordBox`: `Background="{DynamicResource Brush.Background.Panel}"`, `Foreground="{DynamicResource Brush.Foreground.Primary}"`, `BorderBrush="{DynamicResource Brush.Border}"`, `BorderThickness=1`, `Padding=6,4`
- Save `Button`: picks up global Button style automatically
- Status `TextBlock`: `Foreground="{DynamicResource Brush.Semantic.Active}"` (replaces hardcoded `DarkGreen`)

---

## ThemeManager

Static class in `App.xaml.cs`:

```csharp
static class ThemeManager {
    const string DarkUri  = "Resources/DarkTheme.xaml";
    const string LightUri = "Resources/LightTheme.xaml";
    static bool _isDark = true;

    public static void Toggle() {
        var dicts = Application.Current.Resources.MergedDictionaries;
        var current = dicts.First(d => d.Source?.OriginalString.Contains("Theme") == true);
        dicts.Remove(current);
        _isDark = !_isDark;
        dicts.Insert(0, new ResourceDictionary {
            Source = new Uri(_isDark ? DarkUri : LightUri, UriKind.Relative)
        });
    }
}
```

`MainOverlayWindow.xaml.cs` adds:

```csharp
private void ToggleTheme_Click(object sender, RoutedEventArgs e) => ThemeManager.Toggle();
```

Theme state is not persisted — defaults to dark on each launch.

---

## What Is Not Changing

- Window dimensions, layout structure, DockPanel/Grid hierarchy
- Converter classes
- ViewModel bindings
- Cascadia Mono / Consolas font for answer body text
- Window opacity (0.75), `WindowStyle=None`, `Topmost=True`
- GridSplitter height (4px)
- All command wiring

---

## Testing Approach

Visual verification only — no unit tests for XAML styles. After implementation:

1. Launch app in dark mode (default) — verify no hardcoded colors remain visible
2. Click `◐` — verify full theme switch with no residual dark colors
3. Click `◐` again — verify switch back to dark
4. Open Settings — verify it matches active theme
5. Hover each button type — verify hover/pressed states are visible
6. Check all status dots — active (green/dark-green) vs inactive (grey)
7. Collapse and expand sidebar — verify no visual glitches
8. Drag GridSplitter — verify hover color appears
