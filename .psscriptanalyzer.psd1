@{
    Severity = @(
        'Error'
        'Warning'
    )

    ExcludeRules = @(
        # These are interactive build/validation entrypoints; direct console status is intentional.
        'PSAvoidUsingWriteHost'
        # These are private helpers rather than exported PowerShell commands.
        'PSUseSingularNouns'
        # Stop-ProcessTree is an internal cleanup primitive, not an interactive cmdlet.
        'PSUseShouldProcessForStateChangingFunctions'
        # The flagged values are interpolated into embedded Bash build scripts.
        'PSUseDeclaredVarsMoreThanAssignments'
        # Switch parameters consumed in nested script scopes are reported as unused.
        'PSReviewUnusedParameter'
    )
}
