/**
 * Minimal localStorage bridge for Blazor preferences (theme, grid density, etc.).
 */
(function () {
  window.miniWmsPreferences = {
    /** @param {string} key */
    getJson: function (key) {
      try {
        return window.localStorage.getItem(key);
      } catch {
        return null;
      }
    },
    /** @param {string} key @param {string} json */
    saveJson: function (key, json) {
      try {
        window.localStorage.setItem(key, json);
      } catch {
        // Quota / private mode — silently ignore; app still runs with defaults.
      }
    },
  };

  window.miniWmsUi = {
    /** @param {string} message */
    confirm: function (message) {
      return window.confirm(message);
    },
  };
})();
