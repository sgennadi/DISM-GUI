Imports System
Imports System.Windows.Forms

Namespace IndependentDismGui
    Friend Module Program
        <STAThread>
        Sub Main()
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)
            Application.EnableVisualStyles()
            Application.SetCompatibleTextRenderingDefault(False)
            Application.Run(New MainWindow())
        End Sub
    End Module
End Namespace
