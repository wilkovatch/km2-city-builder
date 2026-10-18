namespace Operations {
    //The progress of a long operation: shown on the progress bar and reported to the caller (e.g. the API), if any
    public class ProgressReporter {
        readonly ProgressBar bar;
        readonly System.IProgress<(float value, string text)> progress;
        string text = "";
        float lastReported = -1;

        public ProgressReporter(ProgressBar bar, System.IProgress<(float value, string text)> progress) {
            this.bar = bar;
            this.progress = progress;
        }

        public void SetText(string text) {
            this.text = text;
            bar?.SetText(text);
        }

        public void SetActive(bool active) {
            bar?.SetActive(active);
        }

        public void SetProgress(float value) {
            bar?.SetProgress(value);
            //not more often than every 1%
            if (progress != null && (value < lastReported || value - lastReported >= 0.01f || value >= 1)) {
                lastReported = value;
                progress.Report((value, text));
            }
        }
    }
}
