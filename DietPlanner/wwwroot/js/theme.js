(function () {
    function resolveInitialTheme() {
        var stored = localStorage.getItem("theme");
        if (stored === "dark" || stored === "light") {
            return stored;
        }

        return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
    }

    function applyTheme(theme) {
        document.documentElement.setAttribute("data-theme", theme);
    }

    window.themeManager = {
        get: function () {
            return document.documentElement.getAttribute("data-theme") || resolveInitialTheme();
        },
        set: function (theme) {
            localStorage.setItem("theme", theme);
            applyTheme(theme);
        }
    };

    // Runs synchronously while the page is still parsing <head>, before the
    // render-blocking stylesheet finishes loading, so the correct theme
    // attribute is in place before first paint - no light-mode flash on load.
    applyTheme(resolveInitialTheme());
})();
