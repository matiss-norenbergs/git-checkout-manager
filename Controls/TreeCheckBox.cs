using System.Windows;
using System.Windows.Controls;

namespace GitCheckoutManager.Controls
{
    /// <summary>
    /// Folder checkbox for the tree. The middle state is display-only (some sub-folders selected) and
    /// comes from the binding; a click or key press never produces it: middle or unticked → ticked,
    /// ticked → unticked.
    /// </summary>
    public class TreeCheckBox : CheckBox
    {
        static TreeCheckBox()
        {
            // Keep the stock CheckBox template; the theme's implicit style is applied via BasedOn in XAML.
            DefaultStyleKeyProperty.OverrideMetadata(typeof(TreeCheckBox), new FrameworkPropertyMetadata(typeof(CheckBox)));
        }

        protected override void OnToggle() => SetCurrentValue(IsCheckedProperty, IsChecked != true);
    }
}
