(() => {
    "use strict";

    const protocolVersion = 1;

    function initialize() {
        const openButton = document.getElementById("session-open-exam");
        const finishButton = document.getElementById("session-finish-exam");
        const status = document.getElementById("session-status");
        if (!openButton || !finishButton || !status) {
            return;
        }

        const webview = window.chrome?.webview;
        if (!webview) {
            status.textContent = status.dataset.directBrowser;
            return;
        }

        let statusRequestId = crypto.randomUUID();
        let actionRequestId = null;

        function post(type, requestId) {
            webview.postMessage({
                version: protocolVersion,
                type,
                requestId
            });
        }

        function requestStatus(message) {
            openButton.disabled = true;
            finishButton.disabled = true;
            actionRequestId = null;
            statusRequestId = crypto.randomUUID();
            status.textContent = message || status.dataset.connecting;
            post("clientReady", statusRequestId);
        }

        webview.addEventListener("message", event => {
            const message = event.data;
            if (!message || message.version !== protocolVersion) {
                return;
            }

            if (message.type === "sessionStatus"
                && message.requestId === statusRequestId) {
                const active = message.state === "inExam";
                openButton.disabled = !active;
                finishButton.disabled = !active;
                status.textContent = active
                    ? status.dataset.ready
                    : message.message || status.dataset.unavailable;
                return;
            }

            if (message.requestId !== actionRequestId) {
                return;
            }

            if (message.type === "openExamResult") {
                requestStatus(message.message);
                return;
            }

            if (message.type !== "finishExamResult") {
                return;
            }

            if (message.state === "accepted") {
                status.textContent = message.message || status.dataset.finishing;
                return;
            }

            requestStatus(message.message);
        });

        openButton.addEventListener("click", () => {
            if (openButton.disabled || actionRequestId !== null) {
                return;
            }

            openButton.disabled = true;
            finishButton.disabled = true;
            status.textContent = status.dataset.opening;
            actionRequestId = crypto.randomUUID();
            post("openExam", actionRequestId);
        });

        finishButton.addEventListener("click", () => {
            if (finishButton.disabled || actionRequestId !== null) {
                return;
            }

            openButton.disabled = true;
            finishButton.disabled = true;
            status.textContent = status.dataset.finishing;
            actionRequestId = crypto.randomUUID();
            post("finishExam", actionRequestId);
        });

        post("clientReady", statusRequestId);
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initialize, { once: true });
    } else {
        initialize();
    }
})();
