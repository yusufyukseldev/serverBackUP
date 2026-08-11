// Loaded synchronously from <head> so the attribute is on <html> before the
// first paint — applying it from Blazor after the circuit connects would show
// a flash of the wrong theme on every navigation.
(function () {
    var KEY = 'sb-theme';

    function read() {
        try {
            var value = localStorage.getItem(KEY);
            return value === 'dark' || value === 'light' ? value : 'system';
        } catch (e) {
            // Private mode / storage disabled: fall back to following the OS.
            return 'system';
        }
    }

    function apply() {
        var preference = read();
        if (preference === 'system') {
            document.documentElement.removeAttribute('data-theme');
        } else {
            document.documentElement.setAttribute('data-theme', preference);
        }
    }

    window.sbTheme = {
        get: read,
        set: function (preference) {
            try {
                if (preference === 'dark' || preference === 'light') {
                    localStorage.setItem(KEY, preference);
                } else {
                    localStorage.removeItem(KEY);
                }
            } catch (e) {
                // Storage failure only costs persistence; still switch this tab.
            }
            apply();
            return read();
        }
    };

    apply();
})();
