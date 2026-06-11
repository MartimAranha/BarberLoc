namespace WebApplication1.Models.ViewModels
{
    public class GoogleSyncSummaryViewModel
    {
        public int TotalProcessed { get; set; }
        public int ActiveCount { get; set; }
        public int PermanentlyClosedCount { get; set; }
        public int TemporarilyClosedCount { get; set; }
        public int UnverifiedCount { get; set; }
        public int ErrorCount { get; set; }
    }
}
