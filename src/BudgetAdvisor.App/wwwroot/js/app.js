window.budgetAdvisor = window.budgetAdvisor || {};

window.budgetAdvisor.storage = {
    save: function (key, value) {
        window.localStorage.setItem(key, value);
    },
    load: function (key) {
        return window.localStorage.getItem(key);
    },
    getUsageBytes: function () {
        const encoder = new TextEncoder();
        let total = 0;

        for (let index = 0; index < window.localStorage.length; index++) {
            const key = window.localStorage.key(index) || "";
            const value = window.localStorage.getItem(key) || "";
            total += encoder.encode(key).length;
            total += encoder.encode(value).length;
        }

        return total;
    }
};

window.budgetAdvisor.files = {
    downloadText: function (fileName, content, contentType) {
        const blob = new Blob([content], { type: contentType });
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement("a");
        anchor.href = url;
        anchor.download = fileName;
        anchor.click();
        anchor.remove();
        URL.revokeObjectURL(url);
    }
};
