window.budgetAdvisor = window.budgetAdvisor || {};

window.budgetAdvisor.storage = {
    save: function (key, value) {
        window.localStorage.setItem(key, value);
    },
    load: function (key) {
        return window.localStorage.getItem(key);
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
