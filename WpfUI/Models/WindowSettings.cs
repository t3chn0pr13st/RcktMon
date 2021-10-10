using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace RcktMon.Models
{
    public class WindowSettings
    {
        public WindowState State { get; set; }
        public double Left { get; set; }
        public double Top { get; set; }
        public double Height { get; set; }
        public double Width { get; set; }

        public bool CheckVisible()
        {
            return Left > SystemParameters.VirtualScreenLeft && Left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth 
                && Top > SystemParameters.VirtualScreenTop && Top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight
                && Height <= SystemParameters.VirtualScreenHeight
                && Width <= SystemParameters.VirtualScreenWidth;
        }
    }
}
