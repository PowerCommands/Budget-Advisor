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

window.budgetAdvisor.dropbox = {
    connect: async function (clientId, callbackPath) {
        if (!clientId || !clientId.trim()) {
            throw new Error("A Dropbox app key is required.");
        }

        const trimmedClientId = clientId.trim();
        const redirectUri = new URL(callbackPath || "auth/dropbox/callback", document.baseURI).toString();
        const codeVerifier = window.budgetAdvisor.dropbox.generateCodeVerifier();
        const codeChallenge = await window.budgetAdvisor.dropbox.createCodeChallenge(codeVerifier);
        const state = window.budgetAdvisor.dropbox.generateCodeVerifier();
        const responseStorageKey = `budgetAdvisor.dropbox.auth.response.${state}`;
        const authorizeUrl = new URL("https://www.dropbox.com/oauth2/authorize");

        authorizeUrl.searchParams.set("client_id", trimmedClientId);
        authorizeUrl.searchParams.set("response_type", "code");
        authorizeUrl.searchParams.set("token_access_type", "offline");
        authorizeUrl.searchParams.set("code_challenge_method", "S256");
        authorizeUrl.searchParams.set("code_challenge", codeChallenge);
        authorizeUrl.searchParams.set("redirect_uri", redirectUri);
        authorizeUrl.searchParams.set("state", state);

        const popup = window.open(authorizeUrl.toString(), "budgetAdvisorDropboxConnect", "width=720,height=820,resizable=yes,scrollbars=yes");
        if (!popup) {
            throw new Error("The Dropbox sign-in window was blocked. Allow pop-ups and try again.");
        }

        return await new Promise((resolve, reject) => {
            const timeout = window.setTimeout(() => {
                cleanup();
                reject(new Error("Dropbox authorization timed out."));
            }, 180000);

            const interval = window.setInterval(() => {
                if (popup.closed) {
                    const storedResponse = window.budgetAdvisor.dropbox.consumeAuthorizationResponse(responseStorageKey);
                    if (storedResponse) {
                        complete(storedResponse);
                        return;
                    }
                }
            }, 500);

            const cleanup = () => {
                window.clearTimeout(timeout);
                window.clearInterval(interval);
                window.removeEventListener("message", handleMessage);
                window.localStorage.removeItem(responseStorageKey);
                if (!popup.closed) {
                    popup.close();
                }
            };

            const complete = (message) => {
                if (!message || message.state !== state) {
                    cleanup();
                    reject(new Error("Dropbox authorization state validation failed."));
                    return;
                }

                if (message.error) {
                    cleanup();
                    reject(new Error(message.error));
                    return;
                }

                cleanup();
                resolve({
                    code: message.code || "",
                    codeVerifier: codeVerifier,
                    redirectUri: redirectUri
                });
            };

            const handleMessage = (event) => {
                if (event.origin !== window.location.origin) {
                    return;
                }

                const message = event.data;
                if (!message || message.type !== "budgetAdvisor.dropbox.auth") {
                    return;
                }
                complete(message);
            };

            window.addEventListener("message", handleMessage);
            window.localStorage.removeItem(responseStorageKey);
        });
    },
    consumeAuthorizationResponse: function (storageKey) {
        const json = window.localStorage.getItem(storageKey);
        if (!json) {
            return null;
        }

        try {
            return JSON.parse(json);
        } catch {
            return null;
        }
    },
    generateCodeVerifier: function () {
        const bytes = new Uint8Array(32);
        window.crypto.getRandomValues(bytes);
        return window.budgetAdvisor.dropbox.toBase64Url(bytes);
    },
    createCodeChallenge: async function (codeVerifier) {
        const data = new TextEncoder().encode(codeVerifier);
        const digest = await window.crypto.subtle.digest("SHA-256", data);
        return window.budgetAdvisor.dropbox.toBase64Url(new Uint8Array(digest));
    },
    toBase64Url: function (bytes) {
        let binary = "";
        for (let index = 0; index < bytes.length; index++) {
            binary += String.fromCharCode(bytes[index]);
        }

        return window.btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/g, "");
    }
};
