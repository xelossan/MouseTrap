using MouseTrap.Models;


namespace MouseTrap;

public class Prompt {
    // allowCancel: for changing an *existing* bridge's target, closing the dialog (Cancel, Escape,
    // the X button) leaves it unchanged and returns -1, instead of forcing a pick like the original
    // "add a new bridge" flow does (where there's nothing sensible to cancel back to).
    public static int ChooseScreenDialog(ScreenConfigCollection screens, int screenIdToExclude, bool allowCancel = false)
    {
        var resultId = -1;
        var cancelled = false;
        do {
            var f = new Form {
                Width = 500,
                Height = 200,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowOnly,
                StartPosition = FormStartPosition.CenterScreen,
                Text = "Choose target screen",
                Icon = App.Icon
            };

            if (allowCancel) {
                f.FormClosing += (s, e) => {
                    if (resultId == -1) cancelled = true;
                };
            }

            var container = new FlowLayoutPanel {
                Location = Point.Empty,
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                WrapContents = true
            };

            foreach (var screen in screens) {
                var button = new Button {
                    Text = screen.ScreenNum,
                    Width = 50,
                    Height = 50,
                    Enabled = screen.ScreenId != screenIdToExclude
                };
                button.Click += (sender, e) => {
                    resultId = screen.ScreenId;
                    f.Close();
                };
                container.Controls.Add(button);
            }

            f.Controls.Add(container);

            if (allowCancel) {
                var cancelBtn = new Button { Text = "Cancel", Width = 80, Height = 30 };
                cancelBtn.Click += (s, e) => f.Close();
                container.Controls.Add(cancelBtn);
                f.CancelButton = cancelBtn;
            }

            f.ShowDialog();
        } while (resultId == -1 && !cancelled);

        return cancelled ? -1 : resultId;
    }
}
