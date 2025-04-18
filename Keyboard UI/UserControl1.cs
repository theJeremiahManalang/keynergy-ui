using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Keyboard_UI
{
    public partial class dataHistory : UserControl
    {
        private Dictionary<string, int> rowLookup = new Dictionary<string, int>();

        public dataHistory()
        {
            InitializeComponent();
            //InitializeTable();
        }
        // Add this method to clear existing rows
        public void ClearRows()
        {
            // Remove all rows except header
            while (dataHistoryTable.RowCount > 1)
            {
                dataHistoryTable.RowCount--;
            }

            // Remove all controls except header
            var controlsToRemove = new List<Control>();
            foreach (Control control in dataHistoryTable.Controls)
            {
                var pos = dataHistoryTable.GetPositionFromControl(control);
                if (pos.Row > 0) // Keep header row (row 0)
                {
                    controlsToRemove.Add(control);
                }
            }

            foreach (var control in controlsToRemove)
            {
                dataHistoryTable.Controls.Remove(control);
            }

            rowLookup.Clear();
        }
        public void todayAddOrUpdateRow(string date, int totalKeypresses, double totalCurrent, double totalVoltage)
        {
            todayDateLabel.Text = date;
            todayKeypressesLabel.Text = totalKeypresses.ToString();
            todayCurrentLabel.Text = $"{totalCurrent:F2} mA";
            todayVoltageLabel.Text = $"{totalVoltage:F2} V";

            // If today row doesn't exist yet, add it manually (just once)
            if (dataHistoryTable.RowCount < 2)
            {
                dataHistoryTable.RowCount = 2;
                dataHistoryTable.RowStyles.Insert(1, new RowStyle(SizeType.Absolute, 50F));

                dataHistoryTable.Controls.Add(todayDateLabel, 0, 1);
                dataHistoryTable.Controls.Add(todayKeypressesLabel, 1, 1);
                dataHistoryTable.Controls.Add(todayCurrentLabel, 2, 1);
                dataHistoryTable.Controls.Add(todayVoltageLabel, 3, 1);
            }
        }

        public void AddOrUpdateRow(string date, int totalKeypresses, double totalCurrent, double totalVoltage)
        {
            // If the date already exists, update the row
            if (rowLookup.TryGetValue(date, out int existingRowIndex))
            {
                ((Label)dataHistoryTable.GetControlFromPosition(0, existingRowIndex)).Text = date;
                ((Label)dataHistoryTable.GetControlFromPosition(1, existingRowIndex)).Text = totalKeypresses.ToString();
                ((Label)dataHistoryTable.GetControlFromPosition(2, existingRowIndex)).Text = $"{totalCurrent:F2} mA";
                ((Label)dataHistoryTable.GetControlFromPosition(3, existingRowIndex)).Text = $"{totalVoltage:F2} V";
                return;
            }

            // Shift existing rows down (starting from the last row, excluding header at row 0)
            int dataRowStart = 2; // Leave header untouched
            int newRowIndex = dataRowStart;

            dataHistoryTable.RowCount += 1;
            dataHistoryTable.RowStyles.Insert(newRowIndex, new RowStyle(SizeType.Absolute, 50F));

            for (int i = dataHistoryTable.RowCount - 1; i >= 0; i--)
            {
                var control = dataHistoryTable.Controls[i];
                var pos = dataHistoryTable.GetPositionFromControl(control);

                if (pos.Row >= newRowIndex)
                {
                    dataHistoryTable.SetRow(control, pos.Row + 1);
                }
            }

            // Create new labels
            Label dateLabel = new Label()
            {
                Text = date,
                Anchor = AnchorStyles.None,
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = true
            };

            Label keypressLabel = new Label()
            {
                Text = totalKeypresses.ToString(),
                Anchor = AnchorStyles.None,
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = true
            };

            Label currentLabel = new Label()
            {
                Text = $"{totalCurrent:F2} mA",
                Anchor = AnchorStyles.None,
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = true
            };

            Label voltageLabel = new Label()
            {
                Text = $"{totalVoltage:F2} V",
                Anchor = AnchorStyles.None,
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = true
            };

            // Insert into Row 1 (just under the header)
            dataHistoryTable.Controls.Add(dateLabel, 0, dataRowStart);
            dataHistoryTable.Controls.Add(keypressLabel, 1, dataRowStart);
            dataHistoryTable.Controls.Add(currentLabel, 2, dataRowStart);
            dataHistoryTable.Controls.Add(voltageLabel, 3, dataRowStart);

            // Update row lookup: shift existing indexes +1, then insert the new one
            var updatedLookup = new Dictionary<string, int>();
            foreach (var kvp in rowLookup)
            {
                updatedLookup[kvp.Key] = kvp.Value + 1;
            }
            updatedLookup[date] = newRowIndex;
            rowLookup = updatedLookup;
        }

        
        public class HorizontalScrollOnlyPanel : Panel
        {
            protected override System.Windows.Forms.CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    cp.Style &= ~0x00200000; // WS_VSCROLL
                    return cp;
                }
            }
        }

    }

}