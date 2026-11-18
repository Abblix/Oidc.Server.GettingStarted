#!/usr/bin/env bash
# Follows the Getting Started guide from an empty directory: creates the solution and both projects,
# adds the packages, writes every file the guide writes (copied from the folders next to this script,
# the same files the guide's code blocks are taken from), builds, starts the provider and the client
# and walks the flow a reader walks in the browser: discovery, sign-in as the demo user, the claims
# page, sign-out. Exits non-zero naming the step that failed.
#
# Usage: guide.sh [--abblix-version <version>] [--nuget-source <path or URL>] [<empty directory>]
#   --abblix-version  the Abblix.Oidc.Server.Mvc version to install instead of the latest one
#   --nuget-source    a NuGet source to use besides nuget.org, such as a folder of freshly packed packages;
#                     needs --abblix-version, since otherwise nuget.org's latest release wins
#   <directory>       where to create the solution; the current directory when omitted; must be empty
#
# Needs the .NET 11 SDK, Python 3 and the ports 5001 and 5002 free.

set -Eeuo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
abblix_version=""
nuget_source=""
work_dir="."

while [ "$#" -gt 0 ]; do
  case "$1" in
    --abblix-version) abblix_version="${2:?--abblix-version needs a value}"; shift 2 ;;
    --nuget-source) nuget_source="${2:?--nuget-source needs a value}"; shift 2 ;;
    -h|--help) sed -n '2,14p' "${BASH_SOURCE[0]}"; exit 0 ;;
    -*) echo "guide.sh: unknown option $1" >&2; exit 2 ;;
    *) work_dir="$1"; shift ;;
  esac
done

step="start"
fail() {
  echo "guide.sh: step '$step' failed: $*" >&2
  exit 1
}
# A failure inside a ( ... ) block reaches this trap twice, in the block and in the script; the
# script alone reports it
trap 'status=$?; [ "$BASH_SUBSHELL" -eq 0 ] && echo "guide.sh: step '\''$step'\'' failed (exit code $status)" >&2' ERR

app_pids=()
# dotnet run starts the application as a child process, and that child holds the port, so the whole
# process tree goes; on Windows the tree is only visible to taskkill
tree_of() {
  local child
  echo "$1"
  for child in $(ps -A -o pid= -o ppid= | awk -v parent="$1" '$2 == parent { print $1 }'); do
    tree_of "$child"
  done
}
stop_apps() {
  local pid pids=() alive child
  # Cleanup runs on every exit, a failed step's included: nothing here may stop it half-way
  trap - ERR
  set +e
  for pid in "${app_pids[@]+"${app_pids[@]}"}"; do
    if [ -r "/proc/$pid/winpid" ]; then
      taskkill //F //T //PID "$(cat "/proc/$pid/winpid")" > /dev/null 2>&1
    else
      # A read loop rather than mapfile: macOS ships bash 3.2
      while read -r child; do
        pids+=("$child")
      done < <(tree_of "$pid")
    fi
  done
  [ "${#pids[@]}" -gt 0 ] || return 0
  kill "${pids[@]}" 2> /dev/null
  # The applications shut down gracefully and take a moment to release their ports
  for _ in $(seq 1 20); do
    alive=""
    for pid in "${pids[@]}"; do
      # A process of this script that exited stays listed as a zombie until the script ends
      case "$(ps -o stat= -p "$pid")" in
        "" | Z*) ;;
        *) alive="$pid" ;;
      esac
    done
    [ -n "$alive" ] || break
    sleep 0.5
  done
  kill -9 "${pids[@]}" 2> /dev/null
  return 0
}
trap stop_apps EXIT

step="check the tools"
command -v dotnet > /dev/null || fail "dotnet is not on PATH"
python=""
for candidate in python3 python; do
  if command -v "$candidate" > /dev/null && "$candidate" -c 'import sys; sys.exit(sys.version_info < (3, 9))' 2> /dev/null; then
    python="$candidate"
    break
  fi
done
[ -n "$python" ] || fail "Python 3.9 or later is not on PATH"

if [ -n "$nuget_source" ]; then
  step="check the NuGet source"
  # dotnet add package takes only the latest stable version unless told which one, so a prerelease in
  # the source would lose to nuget.org's release without a word
  [ -n "$abblix_version" ] || fail "--nuget-source needs --abblix-version naming the package version to test"
  # Resolved before the script moves into the new solution, so a relative path means the caller's one
  case "$nuget_source" in
    *://*) ;;
    *)
      resolved_source="$(cd "$nuget_source" && pwd)" || fail "$nuget_source is not a folder"
      nuget_source="$resolved_source"
      ;;
  esac
fi

step="check the directory"
mkdir -p "$work_dir"
cd "$work_dir"
[ -z "$(ls -A)" ] || fail "$(pwd) is not empty"

step="check the ports"
for port in 5001 5002; do
  "$python" - "$port" <<'PY' || fail "port $port is already in use: stop whatever listens on it"
import socket, sys
port = int(sys.argv[1])
for host in ("127.0.0.1", "::1"):
    try:
        with socket.create_connection((host, port), timeout=1):
            sys.exit(1)
    except OSError:
        pass
PY
done

trust_certificate() {
  # #region trust-certificate
  dotnet dev-certs https --trust
  # #endregion
}

step="trust the development certificate"
case "$(uname -s)" in
  Linux)
    # On Linux --trust exits with 4 when it trusted the certificate for OpenSSL, which TestClientApp
    # calls the provider through, but not for every other certificate store it tried
    status=0
    trust_certificate || status=$?
    [ "$status" -eq 0 ] || [ "$status" -eq 4 ] || fail "dotnet dev-certs https --trust exited with $status"
    # .NET on Linux reads trusted certificates from OpenSSL's folders, and --trust put the development
    # certificate into a folder of its own, which the reader adds as that command's output says
    if [ -d /usr/lib/ssl/certs ]; then
      # #region trust-certificate-linux
      export SSL_CERT_DIR="$HOME/.aspnet/dev-certs/trust:/usr/lib/ssl/certs"
      # #endregion
    else
      # Elsewhere the system certificates are reachable through /etc/ssl/certs
      export SSL_CERT_DIR="$HOME/.aspnet/dev-certs/trust:${SSL_CERT_DIR:-/etc/ssl/certs}"
    fi
    ;;
  *)
    # Windows and macOS ask the user to confirm the trust, so it is asked only when not given yet
    if ! dotnet dev-certs https --check --trust > /dev/null; then
      trust_certificate
    fi
    ;;
esac

if [ -n "$nuget_source" ]; then
  step="add the NuGet source"
  # In Git Bash on Windows NuGet takes only a native Windows path for a folder source
  if command -v cygpath > /dev/null && [ "${nuget_source#*://}" = "$nuget_source" ]; then
    nuget_source="$(cygpath -w "$nuget_source")"
  fi
  # Abblix packages come from the extra source only, so one missing there fails the restore instead of
  # arriving from nuget.org; everything else comes from nuget.org
  "$python" - "$nuget_source" <<'PY'
import sys
import xml.etree.ElementTree as ET
configuration = ET.Element("configuration")
sources = ET.SubElement(configuration, "packageSources")
ET.SubElement(sources, "clear")
ET.SubElement(sources, "add", key="nuget.org", value="https://api.nuget.org/v3/index.json")
ET.SubElement(sources, "add", key="guide-extra-source", value=sys.argv[1])
mapping = ET.SubElement(configuration, "packageSourceMapping")
for key, pattern in (("nuget.org", "*"), ("guide-extra-source", "Abblix.*")):
    ET.SubElement(ET.SubElement(mapping, "packageSource", key=key), "package", pattern=pattern)
ET.indent(configuration)
ET.ElementTree(configuration).write("nuget.config", encoding="utf-8", xml_declaration=True)
PY
  # A package already in the machine's NuGet cache wins over the source even when the source holds a
  # newer build of the same version, so this run keeps a cache of its own
  NUGET_PACKAGES="$(pwd)/.nuget-packages"
  export NUGET_PACKAGES
fi

step="create the solution"
# #region create-solution
dotnet new sln -n GettingStarted
# #endregion

step="create OpenIDProviderApp"
# #region create-provider
dotnet new mvc -n OpenIDProviderApp
# #endregion

step="create TestClientApp"
# #region create-client
dotnet new mvc -n TestClientApp
# #endregion

step="add both projects to the solution"
# #region add-to-solution
dotnet sln add ./OpenIDProviderApp/OpenIDProviderApp.csproj
dotnet sln add ./TestClientApp/TestClientApp.csproj
# #endregion

step="set the port numbers"
# The guide changes only the applicationUrl of the https profile and leaves the rest as generated
"$python" - OpenIDProviderApp/Properties/launchSettings.json https://localhost:5001 \
  TestClientApp/Properties/launchSettings.json https://localhost:5002 <<'PY'
import json, sys
args = sys.argv[1:]
for path, url in zip(args[::2], args[1::2]):
    with open(path, encoding="utf-8-sig") as file:
        settings = json.load(file)
    settings["profiles"]["https"]["applicationUrl"] = url
    with open(path, "w", encoding="utf-8") as file:
        json.dump(settings, file, indent=2)
        file.write("\n")
PY

step="add Abblix.Oidc.Server.Mvc to OpenIDProviderApp"
(
  cd OpenIDProviderApp
  if [ -z "$abblix_version" ]; then
    # #region add-abblix-package
    dotnet add package Abblix.Oidc.Server.Mvc
    # #endregion
  else
    # A bare version is a lower bound, which NuGet meets with the nearest higher one and only a
    # warning; the brackets make it exact, so a version missing from the sources fails here
    dotnet add package Abblix.Oidc.Server.Mvc --version "[$abblix_version]"
  fi
# bash 3.2 on macOS does not stop the script when a ( ... ) block fails under set -e
) || fail "dotnet add package exited with $?"
"$python" - OpenIDProviderApp/obj/project.assets.json "$abblix_version" <<'PY' || fail "the restored Abblix packages are not all of the version asked for"
import json, sys
with open(sys.argv[1], encoding="utf-8") as file:
    libraries = [name.split("/") for name in json.load(file)["libraries"]]
abblix = sorted((name, version) for name, version in libraries if name.lower().startswith("abblix."))
print("guide.sh: OpenIDProviderApp uses " + ", ".join(f"{name} {version}" for name, version in abblix))
mvc = [version for name, version in abblix if name.lower() == "abblix.oidc.server.mvc"]
if len(mvc) != 1:
    sys.exit(1)
# The package's own dependencies are lower bounds, so with a version asked for every Abblix package
# must be the one restored for Abblix.Oidc.Server.Mvc, which was asked for exactly
if sys.argv[2] and any(version != mvc[0] for _, version in abblix):
    sys.exit(1)
PY

step="add Microsoft.AspNetCore.Authentication.OpenIdConnect to TestClientApp"
(
  cd TestClientApp
  # #region add-openidconnect-package
  dotnet add package Microsoft.AspNetCore.Authentication.OpenIdConnect
  # #endregion
) || fail "dotnet add package exited with $?"

step="write the files the guide writes"
cp -R "$here/OpenIDProviderApp/." OpenIDProviderApp/
cp -R "$here/TestClientApp/." TestClientApp/

step="build"
dotnet build

start_app() {
  local project="$1"
  (
    cd "$project"
    exec > "../$project.log" 2>&1
    # #region run
    dotnet run -lp https
    # #endregion
  ) &
  app_pids+=("$!")
}

wait_for_app() {
  local project="$1" url="$2" pid="$3" deadline=$((SECONDS + 60))
  while [ "$SECONDS" -lt "$deadline" ]; do
    if ! kill -0 "$pid" 2> /dev/null; then
      tail -n 40 "$project.log" >&2
      fail "$project exited; the end of its output is above"
    fi
    if "$python" - "$url" <<'PY'
import ssl, sys, urllib.error, urllib.request
context = ssl.create_default_context()
context.check_hostname = False
context.verify_mode = ssl.CERT_NONE
class NoRedirects(urllib.request.HTTPRedirectHandler):
    """Takes a redirect as the answer: the client's challenge must not test the provider here."""

    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


opener = urllib.request.build_opener(urllib.request.HTTPSHandler(context=context), NoRedirects())
try:
    opener.open(sys.argv[1], timeout=2)
except urllib.error.HTTPError:
    pass
except OSError:
    sys.exit(1)
PY
    then
      return
    fi
    sleep 1
  done
  tail -n 40 "$project.log" >&2
  fail "$project did not answer at $url within a minute; the end of its output is above"
}

step="start OpenIDProviderApp"
start_app OpenIDProviderApp
wait_for_app OpenIDProviderApp https://localhost:5001/ "${app_pids[0]}"

step="start TestClientApp"
start_app TestClientApp
wait_for_app TestClientApp https://localhost:5002/ "${app_pids[1]}"

step="walk the flow"
# The applications' output tells what the provider or the client refused; -u keeps the flow's own
# lines in order with the failure line
"$python" -u - <<'PY' || { tail -n 40 OpenIDProviderApp.log TestClientApp.log >&2; fail "the flow stopped; its reason and the end of both applications' output are above"; }
import json, ssl, sys, urllib.error, urllib.parse, urllib.request
from html.parser import HTMLParser
from http.cookiejar import CookieJar

PROVIDER = "https://localhost:5001"
CLIENT = "https://localhost:5002"
EMAIL = "john.doe@example.com"
PASSWORD = "Jd!2024$3cur3"
step = "discovery"


def fail(message):
    print(f"guide.sh: step '{step}' failed: {message}", file=sys.stderr)
    sys.exit(1)


class RecordingRedirects(urllib.request.HTTPRedirectHandler):
    """Follows redirects like a browser and remembers every address passed through."""

    def redirect_request(self, req, fp, code, msg, headers, newurl):
        chain.append(newurl)
        return super().redirect_request(req, fp, code, msg, headers, newurl)


class Page(HTMLParser):
    """Collects the inputs of the first form and the text of every list item."""

    def __init__(self, html):
        super().__init__()
        self.form_action = None
        self.inputs = {}
        self.items = []
        self.errors = []
        self._in_form = False
        self._item = None
        self._error = None
        self.feed(html)

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == "form" and self.form_action is None:
            self._in_form = True
            self.form_action = attrs.get("action") or ""
        elif tag == "input" and self._in_form and attrs.get("name"):
            self.inputs[attrs["name"]] = attrs.get("value") or ""
        elif tag == "li":
            self._item = ""
        elif tag == "div" and "validation-summary-errors" in (attrs.get("class") or "").split():
            self._error = ""

    def handle_endtag(self, tag):
        if tag == "form":
            self._in_form = False
        elif tag == "li" and self._item is not None:
            self.items.append(self._item.strip())
            if self._error is not None:
                self.errors.append(self._item.strip())
            self._item = None
        elif tag == "div" and self._error is not None:
            self._error = None

    def handle_data(self, data):
        if self._item is not None:
            self._item += data


context = ssl.create_default_context()
context.check_hostname = False
context.verify_mode = ssl.CERT_NONE
chain = []
browser = urllib.request.build_opener(
    urllib.request.HTTPCookieProcessor(CookieJar()),
    urllib.request.HTTPSHandler(context=context),
    RecordingRedirects())


def visit(url, form=None):
    chain.clear()
    data = urllib.parse.urlencode(form).encode() if form is not None else None
    try:
        with browser.open(url, data=data, timeout=10) as response:
            return response.geturl(), response.read().decode("utf-8")
    except urllib.error.HTTPError as error:
        body = error.read().decode("utf-8", "replace")
        fail(f"{error.geturl()} answered {error.code}: {body[:500]}")
    except OSError as error:
        fail(f"{url} could not be reached: {error}")


def path_of(url):
    parts = urllib.parse.urlsplit(url)
    return f"{parts.scheme}://{parts.netloc}{parts.path}"


def expect_login_page(url, html):
    if path_of(url) != f"{PROVIDER}/Auth/Login":
        fail(f"expected the provider's login page, landed on {url}")
    request_uri = urllib.parse.parse_qs(urllib.parse.urlsplit(url).query).get("request_uri")
    if not request_uri:
        fail(f"the login page address carries no request_uri: {url}")
    page = Page(html)
    for name in ("email", "password", "requestUri"):
        if name not in page.inputs:
            fail(f"the login form has no '{name}' field")
    if page.inputs["requestUri"] != request_uri[0]:
        fail("the login form does not carry the request_uri of its address")
    return page


url, body = visit(f"{PROVIDER}/.well-known/openid-configuration")
try:
    discovery = json.loads(body)
except ValueError:
    fail(f"{url} answered with something other than JSON: {body[:500]}")
if not isinstance(discovery, dict):
    fail(f"{url} answered with JSON that is not an object: {body[:500]}")
issuer = discovery.get("issuer")
if issuer != PROVIDER:
    fail(f"the discovery document names the issuer {issuer!r}, the guide configures {PROVIDER!r}")
print(f"discovery: 200, issuer {issuer}")

step = "open the client"
url, body = visit(f"{CLIENT}/")
login = expect_login_page(url, body)
print(f"open the client: redirected to the login page {path_of(url)}")

step = "sign in"
form = dict(login.inputs, email=EMAIL, password=PASSWORD)
url, body = visit(urllib.parse.urljoin(url, login.form_action), form)
if path_of(url) == f"{PROVIDER}/Auth/Login":
    fail(f"the provider showed the login page again with {Page(body).errors or 'no error'}")
if url != f"{CLIENT}/":
    fail(f"expected to land on {CLIENT}/, landed on {url}")
passed = [path_of(address) for address in chain]
for expected in (f"{PROVIDER}/connect/authorize", f"{CLIENT}/signin-oidc"):
    if expected not in passed:
        fail(f"the sign-in did not pass through {expected}: {passed}")
print(f"sign in: back on {url} through {', '.join(passed)}")

step = "check the claims"
claims = dict(item.split(": ", 1) for item in Page(body).items if ": " in item)
for claim, value in (("sub", "1234567890"), ("name", "John Doe"), ("email", EMAIL)):
    if claims.get(claim) != value:
        fail(f"the claims page shows {claim}={claims.get(claim)!r}, expected {value!r}; it shows {claims}")
for claim in ("sid", "auth_time"):
    if not claims.get(claim):
        fail(f"the claims page shows no {claim}; it shows {claims}")
print(f"check the claims: {', '.join(f'{name}={value}' for name, value in claims.items())}")

step = "sign out"
url, body = visit(f"{CLIENT}/Home/EndSession")
passed = [path_of(address) for address in chain]
for expected in (f"{PROVIDER}/connect/endsession", f"{CLIENT}/signout-callback-oidc", f"{CLIENT}/"):
    if expected not in passed:
        fail(f"the sign-out did not pass through {expected}, it went through {passed} to {url}")
expect_login_page(url, body)
print(f"sign out: through {', '.join(passed)} to the login page")

step = "check the sign-out"
url, body = visit(f"{CLIENT}/")
expect_login_page(url, body)
print("check the sign-out: the client asks for the login again")
PY

step="done"
echo "guide.sh: every step of the guide passed"
