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
        public Form1 ParentFormInstance { get; set; }


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
        public void todayAddOrUpdateRow(string date, int totalKeypresses, double totalVoltage, double totalCurrent)
        {
            todayDateLabel.Text = date;
            todayKeypressesLabel.Text = totalKeypresses.ToString();
            todayVoltageLabel.Text = $"{totalCurrent:F2} V";
            todayCurrentLabel.Text = $"{totalVoltage:F2} mA";



            // If today row doesn't exist yet, add it manually (just once)
            if (dataHistoryTable.RowCount < 2)
            {
                dataHistoryTable.RowCount = 2;
                dataHistoryTable.RowStyles.Insert(1, new RowStyle(SizeType.Absolute, 50F));

                dataHistoryTable.Controls.Add(todayDateLabel, 0, 1);
                dataHistoryTable.Controls.Add(todayKeypressesLabel, 1, 1);
                dataHistoryTable.Controls.Add(todayVoltageLabel, 2, 1);
                dataHistoryTable.Controls.Add(todayCurrentLabel, 3, 1);


            }
        }

        public void AddOrUpdateRow(string date, int totalKeypresses, double totalVoltage, double totalCurrent)
        {
            // If the date already exists, update the row
            if (rowLookup.TryGetValue(date, out int existingRowIndex))
            {
                ((Label)dataHistoryTable.GetControlFromPosition(0, existingRowIndex)).Text = date;
                ((Label)dataHistoryTable.GetControlFromPosition(1, existingRowIndex)).Text = totalKeypresses.ToString();
                ((Label)dataHistoryTable.GetControlFromPosition(2, existingRowIndex)).Text = $"{totalVoltage:F2} V";
                ((Label)dataHistoryTable.GetControlFromPosition(3, existingRowIndex)).Text = $"{totalCurrent:F2} mA";

                return;
            }

            // Add a new row
            int newRowIndex = dataHistoryTable.RowCount;
            dataHistoryTable.RowCount++;
            dataHistoryTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));



            // Create new labels
            Label dateLabel = new Label()
            {
                Text = date,
                Anchor = AnchorStyles.None,
                TextAlign = ContentAlignment.TopCenter,
                AutoSize = true
            };

            Label keypressLabel = new Label()
            {
                Text = totalKeypresses.ToString(),
                Anchor = AnchorStyles.None,
                TextAlign = ContentAlignment.TopCenter,
                AutoSize = true
            };

            Label voltageLabel = new Label()
            {
                Text = $"{totalVoltage:F2} V",
                Anchor = AnchorStyles.None,
                TextAlign = ContentAlignment.TopCenter,
                AutoSize = true
            };

            Label currentLabel = new Label()
            {
                Text = $"{totalCurrent:F2} mA",
                Anchor = AnchorStyles.None,
                TextAlign = ContentAlignment.TopCenter,
                AutoSize = true
            };

            // Add controls to the table
            dataHistoryTable.Controls.Add(dateLabel, 0, newRowIndex);
            dataHistoryTable.Controls.Add(keypressLabel, 1, newRowIndex);
            dataHistoryTable.Controls.Add(voltageLabel, 2, newRowIndex);
            dataHistoryTable.Controls.Add(currentLabel, 3, newRowIndex);


            // Update row lookup
            rowLookup[date] = newRowIndex;

        }

        public void UpdateGrandTotals()
        {
            int totalKeypresses = 0;
            double totalCurrent = 0;
            double totalVoltage = 0;

            // Start from row 2 to skip header (0) and today's row (1)
            for (int row = 1; row < dataHistoryTable.RowCount; row++)
            {
                try
                {
                    // Get controls safely
                    var keypressControl = dataHistoryTable.GetControlFromPosition(1, row);
                    var voltageControl = dataHistoryTable.GetControlFromPosition(2, row);
                    var currentControl = dataHistoryTable.GetControlFromPosition(3, row);


                    if (keypressControl is Label keypressLabel &&
                        currentControl is Label currentLabel &&
                        voltageControl is Label voltageLabel)
                    {
                        var keypressText = keypressLabel.Text;
                        var currentText = currentLabel.Text.Replace(" mA", "").Trim();
                        var voltageText = voltageLabel.Text.Replace(" V", "").Trim();

                        // Parse keypresses
                        if (int.TryParse(keypressText, out int kp))
                            totalKeypresses += kp;

                        // Parse current - handle potential decimal separators
                        if (double.TryParse(currentText, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out double curr))
                            totalCurrent += curr;

                        // Parse voltage - handle potential decimal separators
                        if (double.TryParse(voltageText, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out double volt))
                            totalVoltage += volt;
                        else
                        {
                            // Debugging output
                            Console.WriteLine($"Failed to parse voltage text: '{voltageText}'");
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Log the specific error
                    Console.WriteLine($"Error processing row {row}: {ex.Message}");
                    continue;
                }
            }

            string today = DateTime.Now.ToString("yyyy-MM-dd");

            // Now update the labels you're targeting
            totalDateLabel.Text = today;
            totalKeypressLabel.Text = totalKeypresses.ToString();
            totalCurrentLabel.Text = $"{totalCurrent:F2} mA";
            totalVoltageLabel.Text = $"{totalVoltage:F2} V";

            //ParentFormInstance?.UpdateTotalsUI(totalCurrentLabel, totalVoltageLabel);
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