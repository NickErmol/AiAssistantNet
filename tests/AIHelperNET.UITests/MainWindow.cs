using FlaUI.Core.AutomationElements;

namespace AIHelperNET.UITests;

public sealed class MainWindow(Window window)
{
    // ── Title bar ─────────────────────────────────────────────────────────────
    public Button   BtnToggleSidebar  => Find("Btn_ToggleSidebar").AsButton();
    public Button   BtnToggleSession  => Find("Btn_ToggleSession").AsButton();
    public Button   BtnToggleStealth  => Find("Btn_ToggleStealth").AsButton();
    public Button   BtnToggleTheme    => Find("Btn_ToggleTheme").AsButton();
    public Button   BtnToggleHistory  => Find("Btn_ToggleHistory").AsButton();
    public Button   BtnOpenSettings   => Find("Btn_OpenSettings").AsButton();

    public AutomationElement HeaderStatusText => Find("Header_StatusText");

    // ── Sidebar controls ──────────────────────────────────────────────────────
    public AutomationElement Sidebar         => Find("Sidebar");
    public Button            BtnHideSidebar  => Find("Btn_HideSidebar").AsButton();
    public Button            BtnCapture      => Find("Btn_Capture").AsButton();

    // ── Mode radio buttons ────────────────────────────────────────────────────
    public RadioButton RadioModeAudioOnly   => Find("Mode_AudioOnly").AsRadioButton();
    public RadioButton RadioModeScreenOnly  => Find("Mode_ScreenOnly").AsRadioButton();
    public RadioButton RadioModeAudioScreen => Find("Mode_AudioAndScreen").AsRadioButton();

    // ── Audio source radio buttons ────────────────────────────────────────────
    public RadioButton RadioAudioMicOnly    => Find("AudioSource_MicOnly").AsRadioButton();
    public RadioButton RadioAudioSystemOnly => Find("AudioSource_SystemOnly").AsRadioButton();
    public RadioButton RadioAudioBoth       => Find("AudioSource_Both").AsRadioButton();

    // ── Screen mode radio buttons ─────────────────────────────────────────────
    public RadioButton RadioScreenGeneral      => Find("ScreenMode_General").AsRadioButton();
    public RadioButton RadioScreenSolveCoding  => Find("ScreenMode_SolveCoding").AsRadioButton();
    public RadioButton RadioScreenDebugError   => Find("ScreenMode_DebugError").AsRadioButton();
    public RadioButton RadioScreenExplainCode  => Find("ScreenMode_ExplainCode").AsRadioButton();
    public RadioButton RadioScreenSystemDesign => Find("ScreenMode_SystemDesign").AsRadioButton();
    public RadioButton RadioScreenMultiChoice  => Find("ScreenMode_MultipleChoice").AsRadioButton();

    // ── Status dots ───────────────────────────────────────────────────────────
    public AutomationElement DotMic    => Find("StatusDot_Mic");
    public AutomationElement DotSystem => Find("StatusDot_System");
    public AutomationElement DotOCR    => Find("StatusDot_OCR");
    public AutomationElement DotAI     => Find("StatusDot_AI");

    // ── Panels ────────────────────────────────────────────────────────────────
    public AutomationElement? HistoryPanel =>
        window.FindFirstDescendant(cf => cf.ByAutomationId("Panel_History"));

    // ── Turn cards ────────────────────────────────────────────────────────────
    public AutomationElement? FirstTurnCard =>
        window.FindFirstDescendant(cf => cf.ByAutomationId("TurnCard"));

    // ── Helpers ───────────────────────────────────────────────────────────────
    public bool IsDotActive(AutomationElement dot) =>
        dot.Properties.HelpText.ValueOrDefault == "active";

    public string SessionStatus =>
        HeaderStatusText.Properties.Name.ValueOrDefault ?? string.Empty;

    private AutomationElement Find(string id) =>
        window.FindFirstDescendant(cf => cf.ByAutomationId(id));
}
