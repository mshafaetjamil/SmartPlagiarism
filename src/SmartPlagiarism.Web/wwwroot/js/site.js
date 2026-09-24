// Analysis status polling.
//
// A page showing submissions that are still queued or processing renders a
// hidden [data-analysis-poll-url] element (Views/Shared/_AnalysisPoller). While
// it is present, the status endpoint is asked every few seconds; as soon as any
// submission's status differs from what the page shows, the page reloads to
// display the new state and, once analysis is done, its result.
(() => {
    const poller = document.querySelector('[data-analysis-poll-url]');

    if (!poller) {
        return;
    }

    const url = poller.dataset.analysisPollUrl;
    const rendered = JSON.parse(poller.dataset.analysisPending);
    const intervalMs = 3000;

    // A signed-out session or a server restart should not leave the page
    // hammering the endpoint forever; a few failures in a row stop polling.
    const maxConsecutiveFailures = 5;
    let failures = 0;

    const timer = setInterval(async () => {
        try {
            const response = await fetch(url, {
                headers: { Accept: 'application/json' },
                credentials: 'same-origin',
                cache: 'no-store',
            });

            // A redirect to the login page comes back as HTML, not JSON.
            const isJson = (response.headers.get('content-type') || '').includes('application/json');

            if (!response.ok || !isJson) {
                throw new Error(`Status request failed: ${response.status}`);
            }

            failures = 0;
            const statuses = await response.json();

            if (statuses.some((entry) => rendered[String(entry.id)] !== entry.status)) {
                clearInterval(timer);
                window.location.reload();
            }
        } catch {
            failures += 1;

            if (failures >= maxConsecutiveFailures) {
                clearInterval(timer);
            }
        }
    }, intervalMs);
})();
