// Default Blazor Server reconnection gives up after 8 retries spaced 0/2/2/2... seconds apart
// (~16s total), which isn't enough time for a Fly machine restart (deploy, OOM, crash) to come
// back up. Once retries are exhausted, Blazor shows its "Reload" overlay and stops trying on its
// own - the user has to click it manually. Widening the retry budget gives the machine time to
// restart and the circuit to recover without the user ever seeing that prompt.
Blazor.start({
    circuit: {
        reconnectionOptions: {
            maxRetries: 30,
            retryIntervalMilliseconds: 2000
        }
    }
});
