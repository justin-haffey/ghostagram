let activeRegistration;
let nextRegistrationToken = 0;

export function start(dotNetReference) {
    // A module can be started more than once during reconnects. Replace the old listener
    // first, and let the identity check below ignore callbacks already queued by the browser.
    if (activeRegistration) {
        stop(activeRegistration.token);
    }

    const registration = { token: ++nextRegistrationToken };
    const updateViewport = () => {
        if (activeRegistration === registration) {
            void dotNetReference.invokeMethodAsync("UpdateViewportWidth", window.innerWidth).catch(() => {});
        }
    };
    registration.resizeHandler = updateViewport;
    registration.mediaQuery = window.matchMedia("(max-width: 1279px)");
    registration.mediaQueryHandler = updateViewport;

    activeRegistration = registration;
    window.addEventListener("resize", registration.resizeHandler, { passive: true });
    registration.mediaQuery.addEventListener("change", registration.mediaQueryHandler);
    updateViewport();
    return registration.token;
}

export function stop(token) {
    // Tokens prevent a stale component instance from removing listeners owned by a newer one.
    if (!activeRegistration || activeRegistration.token !== token) {
        return;
    }

    window.removeEventListener("resize", activeRegistration.resizeHandler);
    activeRegistration.mediaQuery.removeEventListener("change", activeRegistration.mediaQueryHandler);
    activeRegistration = undefined;
}

export function focusDrawerTrigger(mode) {
    document.querySelector(`[data-drawer-trigger="${mode}"]`)?.focus();
}
