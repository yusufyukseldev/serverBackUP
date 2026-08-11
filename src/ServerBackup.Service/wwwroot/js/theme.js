// Loaded synchronously from <head> so the attribute is on <html> before the
// first paint — applying it from Blazor after the circuit connects would show
// a flash of the wrong theme on every navigation.
(function () {
    var KEY = 'sb-theme';

    function stored() {
        try {
            var value = localStorage.getItem(KEY);
            return value === 'dark' || value === 'light' ? value : null;
        } catch (e) {
            // Private mode / storage disabled: fall back to following the OS.
            return null;
        }
    }

    function systemTheme() {
        return window.matchMedia && window.matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark';
    }

    // What the user actually sees, which is what the toggle must reflect: the
    // stored choice if there is one, otherwise whatever the OS is asking for.
    function effective() {
        return stored() || systemTheme();
    }

    function apply() {
        var preference = stored();
        if (preference) {
            document.documentElement.setAttribute('data-theme', preference);
        } else {
            document.documentElement.removeAttribute('data-theme');
        }
    }

    window.sbTheme = {
        get: effective,
        set: function (preference) {
            try {
                localStorage.setItem(KEY, preference);
            } catch (e) {
                // Storage failure only costs persistence; still switch this tab.
            }
            apply();
            return effective();
        },
        toggle: function () {
            return window.sbTheme.set(effective() === 'dark' ? 'light' : 'dark');
        }
    };

    apply();
})();
