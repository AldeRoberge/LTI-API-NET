# Place this file in /api/.well-known/jwks.json to serve JWKS for JWT validation
# This script generates a new RSA key pair and saves the public key in JWKS format.

# Create 2048-bit RSA key
$rsa = [System.Security.Cryptography.RSA]::Create(2048)
$parameters = $rsa.ExportParameters($true)

# Convert modulus and exponent to Base64Url
function To-Base64Url([byte[]] $bytes) {
    $b64 = [System.Convert]::ToBase64String($bytes)
    $b64 = $b64.TrimEnd("=")
    $b64 = $b64.Replace("+","-").Replace("/","_")
    return $b64
}

$n = To-Base64Url $parameters.Modulus
$e = To-Base64Url $parameters.Exponent

# Create JWKS JSON
$jwks = @{
    keys = @(
        @{
            kty = "RSA"
            kid = "key1"
            use = "sig"
            alg = "RS256"
            n   = $n
            e   = $e
        }
    )
}

# Save JWKS
$jwks | ConvertTo-Json -Depth 10 | Out-File -Encoding UTF8 jwks.json

Write-Output "JWKS file created: $(Resolve-Path jwks.json)"
Write-Output "Private key (RSA parameters) kept in PowerShell object for signing JWTs"
