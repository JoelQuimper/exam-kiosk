(() => {
    "use strict";

    const protocolVersion = 4;

    function initialize() {
        const startButtons = Array.from(
            document.querySelectorAll(".launcher-start-exam"));
        const status = document.getElementById("launcher-status");
        const antiforgeryToken = document.querySelector(
            "#launcher-antiforgery input[name='__RequestVerificationToken']")?.value;
        if (startButtons.length === 0 || !status || !antiforgeryToken) {
            return;
        }

        const webview = window.chrome?.webview;
        if (!webview) {
            status.textContent = status.dataset.directBrowser;
            return;
        }

        let statusRequestId = crypto.randomUUID();
        let startRequestId = null;

        function setStartButtonsDisabled(disabled) {
            startButtons.forEach(button => {
                button.disabled = disabled;
            });
        }

        function post(type, requestId, additionalData = {}) {
            webview.postMessage({
                version: protocolVersion,
                type,
                requestId,
                ...additionalData
            });
        }

        function requestAgentStatus(message) {
            statusRequestId = crypto.randomUUID();
            status.textContent = message || status.dataset.connecting;
            post("clientReady", statusRequestId);
        }

        webview.addEventListener("message", event => {
            const message = event.data;
            if (!message || message.version !== protocolVersion) {
                return;
            }

            if (message.type === "agentStatus"
                && message.requestId === statusRequestId) {
                const ready = message.state === "available";
                setStartButtonsDisabled(!ready);
                status.textContent = ready
                    ? status.dataset.ready
                    : message.message || status.dataset.unavailable;
                return;
            }

            if (message.type !== "startExamResult"
                || message.requestId !== startRequestId) {
                return;
            }

            if (message.state === "cancelled"
                || message.state === "failed"
                || message.state === "busy") {
                startRequestId = null;
                requestAgentStatus(message.message);
                return;
            }

            status.textContent = message.message || status.dataset.starting;
        });

        startButtons.forEach(startButton => {
            startButton.addEventListener("click", async () => {
                if (startButton.disabled || startRequestId !== null) {
                    return;
                }

                setStartButtonsDisabled(true);
                status.textContent = status.dataset.starting;
                startRequestId = crypto.randomUUID();
                try {
                    const assignmentId = startButton.dataset.assignmentId;
                    const response = await fetch(
                        `/api/v1/exam-assignments/${encodeURIComponent(assignmentId)}/sessions`,
                        {
                            method: "POST",
                            credentials: "same-origin",
                            headers: {
                                "Accept": "application/json",
                                "X-XSRF-TOKEN": antiforgeryToken
                            }
                        });
                    if (response.status !== 201) {
                        throw new Error(
                            `Session creation failed with status ${response.status}.`);
                    }

                    const session = await response.json();
                    if (!session?.sessionId
                        || session.state !== "starting"
                        || !session.profile?.exam?.title) {
                        throw new Error("Session creation returned an invalid response.");
                    }

                    post("startExam", startRequestId, {
                        exam: {
                            title: session.profile.exam.title,
                            sessionId: session.sessionId,
                            profile: session.profile
                        }
                    });
                } catch (error) {
                    console.error("Unable to prepare the exam session.", error);
                    startRequestId = null;
                    status.textContent = status.dataset.unavailable;
                    setStartButtonsDisabled(false);
                }
            });
        });

        status.textContent = status.dataset.connecting;
        post("clientReady", statusRequestId);
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initialize, { once: true });
    } else {
        initialize();
    }
})();
