(() => {
    document.querySelectorAll('form[data-dcr-submit-once]').forEach(form => {
        form.addEventListener('submit', event => {
            if (event.defaultPrevented) return;
            if (form.dataset.submitting === 'true') {
                event.preventDefault();
                return;
            }

            form.dataset.submitting = 'true';
            form.setAttribute('aria-busy', 'true');
            const submitter = event.submitter;
            // Disabled buttons are excluded from the POST. Preserve Save vs Submit explicitly.
            if (submitter && submitter.name) {
                const action = document.createElement('input');
                action.type = 'hidden';
                action.name = submitter.name;
                action.value = submitter.value;
                action.dataset.dcrSubmitAction = 'true';
                form.appendChild(action);
            }
            form.querySelectorAll('button[type="submit"], input[type="submit"]').forEach(button => {
                if (!button.disabled) {
                    button.dataset.dcrSubmitDisabled = 'true';
                    button.disabled = true;
                }
            });
        });
    });

    // Browsers may restore disabled controls when navigating back from the result page.
    window.addEventListener('pageshow', () => {
        document.querySelectorAll('form[data-dcr-submit-once]').forEach(form => {
            delete form.dataset.submitting;
            form.removeAttribute('aria-busy');
            form.querySelectorAll('[data-dcr-submit-disabled]').forEach(button => {
                button.disabled = false;
                delete button.dataset.dcrSubmitDisabled;
            });
            form.querySelectorAll('[data-dcr-submit-action]').forEach(input => input.remove());
        });
    });
})();
