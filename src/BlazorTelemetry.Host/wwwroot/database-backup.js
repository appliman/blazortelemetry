export function download(url, jobId) {
    const key = "blazor-telemetry-backup-download";
    try {
        if (sessionStorage.getItem(key) === jobId) {
            return;
        }
    } catch {
        // Downloads still work when browser storage is disabled.
    }
    const link = document.createElement("a");
    link.href = url;
    link.download = "";
    link.dataset.enhanceNav = "false";
    document.body.appendChild(link);
    link.click();
    link.remove();
    try {
        sessionStorage.setItem(key, jobId);
    } catch {
        // The direct download link remains available.
    }
}
