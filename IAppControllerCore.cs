using Monitorian.Core.Models.Monitor;

namespace MonitorBrightness
{
    public interface IAppControllerCore
    {
        public void OnMonitorAccessFailed(AccessResult result);
        public void OnMonitorsChangeFound();
    }
}