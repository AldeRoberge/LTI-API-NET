openssl req -x509 -nodes -days 365 -newkey rsa:2048 -keyout tls.key -out tls.crt -subj "/CN=localhost"

echo You will need to trust the generated certificate in your browser to avoid security warnings.
echo In Chrome, you can go to chrome://certificate-manager/localcerts/usercerts and import the tls.crt file.