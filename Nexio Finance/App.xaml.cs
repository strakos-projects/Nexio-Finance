using System.Configuration;
using System.Data;
using System.Globalization;
using System.Threading;
using System.Windows;

namespace Nexio_Finance
{
    public partial class App : Application
    {
        // Konstruktor se spustí úplně jako první, ještě před načtením oken
        public App()
        {
            /*
            var culture = new CultureInfo("cs");
Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            Nexio_Finance.Properties.Resources.Culture = culture;*/
        }
    }
}