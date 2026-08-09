using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Xaml.Behaviors;

namespace RcktMon.Views
{
    public class PasswordBehavior : Behavior<TextBox>
    {
        public const string PassReplacement = "**********";

        protected override void OnAttached()
        {
            base.OnAttached();
            this.AssociatedObject.GotFocus += AssociatedObjectOnGotFocus;
            this.AssociatedObject.LostFocus += AssociatedObjectOnLostFocus;
        }

        private void AssociatedObjectOnLostFocus(object sender, RoutedEventArgs e)
        {
            if (AssociatedObject.Text == "")
                AssociatedObject.Text = PassReplacement;
        }

        private void AssociatedObjectOnGotFocus(object sender, RoutedEventArgs e)
        {
            if (AssociatedObject.Text == PassReplacement)
                AssociatedObject.Text = "";
        }

        protected override void OnDetaching()
        {
            this.AssociatedObject.GotFocus -= AssociatedObjectOnGotFocus;
            this.AssociatedObject.LostFocus -= AssociatedObjectOnLostFocus;
        }
    }
}
