using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Automation.Provider;

namespace Taskboard.Desktop;

// The independently rendered child input surface needs its own automation root.
// A plain Grid has no peer, leaving all descendants invisible to Windows UIA.
internal sealed class SurfaceContentRoot : Grid
{
    protected override AutomationPeer OnCreateAutomationPeer() => new ContentPeer(this);
    internal IRawElementProviderSimple NativeProvider() => ((ContentPeer)UIElementAutomationPeer.CreatePeerForElement(this)).Provider();
    private sealed class ContentPeer : FrameworkElementAutomationPeer
    {
        public ContentPeer(SurfaceContentRoot owner) : base(owner) { }
        internal IRawElementProviderSimple Provider() => ProviderFromPeer(this);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Pane;
        protected override string GetClassNameCore() => "TaskboardContent";
        protected override string GetNameCore() => "任务面板内容";
        protected override HostedWindowWrapper? GetHostRawElementProviderCore()
            => PresentationSource.FromVisual((SurfaceContentRoot)Owner) is HwndSource source ? new HostedWindowWrapper(source.Handle) : null;
    }
}
