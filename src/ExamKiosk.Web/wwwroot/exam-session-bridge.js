(() => {
    "use strict";

    const protocolVersion = 2;

    function initialize() {
        const openButton = document.getElementById("session-open-exam");
        const finishButton = document.getElementById("session-finish-exam");
        const status = document.getElementById("session-status");
        const examTitle = document.getElementById("session-exam-title");
        const antiforgeryToken = document.querySelector(
            "#session-antiforgery input[name='__RequestVerificationToken']")?.value;
        if (!openButton || !finishButton || !status || !examTitle
            || !antiforgeryToken) {
            return;
        }

        const webview = window.chrome?.webview;
        if (!webview) {
            status.textContent = status.dataset.directBrowser;
            return;
        }

        let statusRequestId = crypto.randomUUID();
        let actionRequestId = null;
        let activeSessionId = null;

        function post(type, requestId, sessionId = null) {
            const message = {
                version: protocolVersion,
                type,
                requestId
            };
            if (sessionId) {
                message.sessionId = sessionId;
            }
            webview.postMessage(message);
        }

        function requestStatus(message) {
            openButton.disabled = true;
            finishButton.disabled = true;
            actionRequestId = null;
            activeSessionId = null;
            statusRequestId = crypto.randomUUID();
            status.textContent = message || status.dataset.connecting;
            post("clientReady", statusRequestId);
        }

        async function activateSession() {
            const response = await fetch(
                "/api/v1/exam-sessions/active/activate",
                {
                    method: "POST",
                    credentials: "same-origin",
                    headers: { "X-XSRF-TOKEN": antiforgeryToken }
                });
            if (!response.ok) {
                throw new Error(
                    `Session activation failed with status ${response.status}.`);
            }

            const session = await response.json();
            if (!session.sessionId || session.state !== "active") {
                throw new Error("Session activation returned an invalid response.");
            }

            return session;
        }

        async function completeActiveSession() {
            const response = await fetch(
                "/api/v1/exam-sessions/active/complete",
                {
                    method: "POST",
                    credentials: "same-origin",
                    headers: { "X-XSRF-TOKEN": antiforgeryToken }
                });
            if (!response.ok) {
                throw new Error(
                    `Session completion failed with status ${response.status}.`);
            }
        }

        webview.addEventListener("message", async event => {
            const message = event.data;
            if (!message || message.version !== protocolVersion) {
                return;
            }

            if (message.type === "sessionStatus"
                && message.requestId === statusRequestId) {
                const active = message.state === "inExam";
                if (!active) {
                    openButton.disabled = true;
                    finishButton.disabled = true;
                    status.textContent =
                        message.message || status.dataset.unavailable;
                    return;
                }

                try {
                    const session = await activateSession();
                    activeSessionId = session.sessionId;
                    examTitle.textContent = session.examTitle;
                    examTitle.hidden = false;
                    openButton.disabled = false;
                    finishButton.disabled = false;
                    status.textContent = status.dataset.ready;
                } catch (error) {
                    console.error("Unable to activate the exam session.", error);
                    status.textContent = status.dataset.activationFailed;
                }
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
                try {
                    await completeActiveSession();
                    status.textContent = message.message || status.dataset.finishing;
                } catch (error) {
                    console.error("Unable to complete the exam session.", error);
                    status.textContent = status.dataset.completionFailed;
                }
                return;
            }

            requestStatus(message.message);
        });

        openButton.addEventListener("click", () => {
            if (openButton.disabled || actionRequestId !== null
                || !activeSessionId) {
                return;
            }

            openButton.disabled = true;
            finishButton.disabled = true;
            status.textContent = status.dataset.opening;
            actionRequestId = crypto.randomUUID();
            post("openExam", actionRequestId, activeSessionId);
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
