(function () {
    var storageKey = "sidebar";

    // 768px must match $tablet in Styles/_breakpoints.scss; 1024px only picks
    // the default sidebar state, so it has no CSS counterpart.
    var tabletUpQuery = window.matchMedia("(min-width: 768px)");
    var desktopQuery = window.matchMedia("(min-width: 1024px)");

    // localStorage throws when the browser blocks site data, so the choice is
    // also kept in memory - the toggle then still works until the page reloads.
    var sessionPreference = null;

    function storedPreference() {
        var stored = sessionPreference;
        try {
            stored = localStorage.getItem(storageKey) || stored;
        } catch (e) {
        }
        return stored === "expanded" || stored === "rail" ? stored : null;
    }

    function storePreference(value) {
        sessionPreference = value;
        try {
            localStorage.setItem(storageKey, value);
        } catch (e) {
        }
    }

    // Without a saved choice the default follows the viewport, so it's
    // re-applied on resize: tablets start as the icon rail, desktops expanded.
    function applySidebarState() {
        var state = storedPreference() || (desktopQuery.matches ? "expanded" : "rail");
        document.documentElement.setAttribute("data-sidebar", state);
    }

    function setDrawerOpen(open) {
        if (open) {
            document.documentElement.setAttribute("data-sidebar-drawer", "open");
        } else {
            document.documentElement.removeAttribute("data-sidebar-drawer");
        }
    }

    function isDrawerOpen() {
        return document.documentElement.getAttribute("data-sidebar-drawer") === "open";
    }

    // Handled with delegated DOM listeners rather than Blazor @onclick so the
    // toggle works before the SignalR circuit connects, and the state lives on
    // <html> (like the theme) where it can be applied before first paint and
    // survives Blazor re-rendering the layout.
    document.addEventListener("click", function (event) {
        var target = event.target instanceof Element ? event.target : null;
        if (!target) {
            return;
        }

        if (target.closest("[data-sidebar-toggle]")) {
            if (!tabletUpQuery.matches) {
                setDrawerOpen(!isDrawerOpen());
            } else {
                var current = document.documentElement.getAttribute("data-sidebar");
                storePreference(current === "rail" ? "expanded" : "rail");
                applySidebarState();
            }
        } else if (target.closest("[data-sidebar-close]") || target.closest(".nav__link")) {
            setDrawerOpen(false);
        }
    });

    document.addEventListener("keydown", function (event) {
        if (event.key === "Escape") {
            setDrawerOpen(false);
        }
    });

    tabletUpQuery.addEventListener("change", function () {
        setDrawerOpen(false);
    });
    desktopQuery.addEventListener("change", applySidebarState);

    applySidebarState();
})();
