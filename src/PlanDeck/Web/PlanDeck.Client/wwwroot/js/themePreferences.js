(function () {
    'use strict';

    var storageKey = 'PlanDeck.Theme';

    function isValidTheme(value) {
        return value === 'light' || value === 'dark';
    }

    function resolveSystemPreference() {
        if (window.matchMedia && window.matchMedia('(prefers-color-scheme: light)').matches) {
            return 'light';
        }

        return 'dark';
    }

    window.getPlanDeckThemePreference = function () {
        var stored = null;
        try {
            stored = window.localStorage.getItem(storageKey);
        } catch (e) {
            stored = null;
        }

        if (isValidTheme(stored)) {
            return stored;
        }

        return resolveSystemPreference();
    };

    window.setPlanDeckThemePreference = function (theme) {
        if (!isValidTheme(theme)) {
            return Promise.reject(new Error('Theme must be "light" or "dark".'));
        }

        try {
            window.localStorage.setItem(storageKey, theme);
            return Promise.resolve();
        } catch (e) {
            return Promise.reject(e);
        }
    };
})();
