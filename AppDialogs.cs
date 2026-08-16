using Avalonia;
using Avalonia.Controls;
using Material.Dialog;
using Material.Dialog.Enums;
using arknights_random_team.Views;

namespace arknights_random_team;

public static class AppDialogs
{
    public static Window? FindWindow(this Visual visual) => TopLevel.GetTopLevel(visual) as Window;

    public static async Task Alert(Window? owner, string message, string title = "提示")
    {
        var dialog = DialogHelper.CreateAlertDialog(new AlertDialogBuilderParams
        {
            WindowTitle = title,
            ContentHeader = title,
            SupportingText = message,
            DialogButtons = DialogHelper.CreateSimpleDialogButtons(DialogButtonsEnum.Ok)
        });

        if (owner != null)
            await dialog.ShowDialog(owner);
        else
            await dialog.Show();
    }

    public static async Task<bool> Confirm(Window? owner, string message)
    {
        var dialog = new ConfirmDialog(message);
        if (owner != null)
            return await dialog.ShowDialog<bool>(owner);

        dialog.Show();
        return false;
    }
}
