@echo off

echo Starting ADG.LTI in Docker...
echo Open http://localhost/my/ in your browser to access Moodle.
echo You can then use the user 'user' and password 'bitnami' to log in.


docker compose up
pause