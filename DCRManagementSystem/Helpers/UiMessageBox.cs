namespace DCRManagementSystem.Helpers;

public static class UiMessageBox
{
    public static DialogResult Show(string text) => MessageBox.Show(UiLanguageManager.TranslateText(text));
    public static DialogResult Show(string text, string caption) => MessageBox.Show(UiLanguageManager.TranslateText(text), UiLanguageManager.TranslateText(caption));
    public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        => MessageBox.Show(UiLanguageManager.TranslateText(text), UiLanguageManager.TranslateText(caption), buttons, icon);
    public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton)
        => MessageBox.Show(UiLanguageManager.TranslateText(text), UiLanguageManager.TranslateText(caption), buttons, icon, defaultButton);
    public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        => MessageBox.Show(owner, UiLanguageManager.TranslateText(text), UiLanguageManager.TranslateText(caption), buttons, icon);
    public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton)
        => MessageBox.Show(owner, UiLanguageManager.TranslateText(text), UiLanguageManager.TranslateText(caption), buttons, icon, defaultButton);
}
