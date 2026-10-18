using System.Threading.Tasks;
using SM = StringManager;

namespace Operations {
    //The decisions a long operation (e.g. loading a city) needs from whoever started it: the editor asks the user
    //with its popups, the API answers from the options of the call
    public interface IInteraction {
        //a yes/no question (e.g. migrating a city made with an older core)
        Task<bool> Confirm(string titleKey, string messageKey);

        //an error that stops the operation (the message is shown as it is)
        Task ShowError(string message);

        //true if Python and the modules of the core are available. retry: run again the operation that needed them,
        //for interactions that install them later
        Task<bool> EnsurePythonForCore(string core, System.Action retry);
    }

    public class UiInteraction : IInteraction {
        readonly CityBuilderMenuBar menuBar;

        public UiInteraction(CityBuilderMenuBar menuBar) {
            this.menuBar = menuBar;
        }

        //a new alert is not shown while the previous one is (e.g. when called from its button)
        async Task<bool> WaitForPreviousAlert() {
            for (int i = 0; i < 10 && menuBar.AlertActive(); i++) {
                await menuBar.ToSignal(menuBar.GetTree(), "process_frame");
            }
            return !menuBar.AlertActive();
        }

        public async Task<bool> Confirm(string titleKey, string messageKey) {
            if (!await WaitForPreviousAlert()) return false;
            var tcs = new TaskCompletionSource<bool>();
            menuBar.CreateAlert(SM.Get(titleKey), SM.Get(messageKey), SM.Get("YES"), SM.Get("NO"), () => tcs.TrySetResult(true), () => tcs.TrySetResult(false), 250);
            return await tcs.Task;
        }

        public async Task ShowError(string message) {
            if (!await WaitForPreviousAlert()) return;
            var tcs = new TaskCompletionSource<bool>();
            menuBar.CreateAlert(SM.Get("ERROR"), message, SM.Get("OK"), () => tcs.TrySetResult(true), 250);
            await tcs.Task;
        }

        public Task<bool> EnsurePythonForCore(string core, System.Action retry) {
            return Task.FromResult(PythonManager.CheckPythonForCore(menuBar, core, retry));
        }
    }

    //never asks: the answers are given in advance
    public class FixedInteraction : IInteraction {
        public bool allowMigration = false;
        public bool installPythonModules = false;
        public string lastError = null;

        public Task<bool> Confirm(string titleKey, string messageKey) {
            return Task.FromResult(allowMigration);
        }

        public Task ShowError(string message) {
            lastError = message;
            return Task.CompletedTask;
        }

        public async Task<bool> EnsurePythonForCore(string core, System.Action retry) {
            if (PythonManager.IsReadyForCore(core)) return true;
            if (!installPythonModules || !PythonManager.IsPythonReady()) return false;
            await PythonManager.InstallCoreRequirementsAsync(core);
            return PythonManager.IsReadyForCore(core);
        }
    }
}
