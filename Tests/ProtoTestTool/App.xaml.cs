using System.Windows;

namespace ProtoTestTool
{
    public partial class App : Application
    {
        public App()
        {
            DispatcherUnhandledException += App_DispatcherUnhandledException;
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            // Known AvalonEdit Crash workaround
            if (e.Exception is System.NullReferenceException && 
                e.Exception.StackTrace?.Contains("ICSharpCode.AvalonEdit.CodeCompletion.CompletionList.SelectItemFiltering") == true)
            {
                e.Handled = true;
            }
        }
    }
}