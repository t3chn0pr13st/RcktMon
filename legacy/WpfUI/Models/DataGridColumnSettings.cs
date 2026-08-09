using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RcktMon.Models
{
    public record DataGridColumnSettings(
        string GridName,
        string ColumnName,
        double ColumnWidth,
        int DisplayIndex);
}
