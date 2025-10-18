# 🎓 **KEYNERGY: Solenoid-Based Energy-Harvesting Custom Mechanical Keyboard**

**A functional prototype for a sustainable mechanical keyboard that converts keypresses into electrical energy via electromagnetic induction.**

---

## ✨ Key Project Features

### 1. Energy Harvesting & Storage
The system uses **custom solenoid switches** to generate power from keypresses, storing it in a capacitor and Li-ion battery for reuse.

* **Feasibility:** Prototype successfully validates the concept of energy harvesting in an input device.
* **Charging:** Demonstrated ability to illuminate a low-power LED, requiring **25–30 presses** and sustaining light for an average of **10.83 seconds**.
* **Tactile Feedback:** Maintains an acceptable feel, with user weighted mean of **3.49**.

### 2. Real-Time GUI Monitoring
A dedicated Graphical User Interface provides real-time user feedback on keyboard activity and energy generation.

![Keynergy GUI showing keypress heatmaps and energy data](images/keynergy-ui.jpg)

* **Visual Feedback:** Numeric keys are color-coded based on usage intensity (e.g., **Light Red** for 400+ presses).
* **Effectiveness:** GUI was rated highly in user surveys (weighted mean of **3.88**).

---

## 📺 Video Summary & Full Report

For a dynamic overview, demonstration, and detailed findings, view our video and the complete thesis document.

**[Watch the Keynergy Thesis Video Presentation on YouTube](https://www.youtube.com/watch?v=6ZsAPNvZQUU&t=69s)**

The final thesis document is available in the **`/report`** directory.

---

## 💻 Get Started

### Project Structure

| Directory | Content |
| :--- | :--- |
| **`/report`** | The **final thesis document** (PDF) and defense presentation slides. |
| **`/code`** | Source code for the prototype **firmware** and the **GUI** application. |

### Installation

```bash
git clone [https://github.com/YourGitHubUsername/KeynergyThesis.git](https://github.com/YourGitHubUsername/KeynergyThesis.git)
cd KeynergyThesis
# Run the GUI (example)
pip install -r requirements.txt
python code/gui/main_keynergy_gui.py
