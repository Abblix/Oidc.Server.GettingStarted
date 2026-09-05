// Bundle Bootstrap locally instead of from a CDN. Importing the package registers
// Bootstrap's data-api (which drives the navbar toggler and other data-bs-* widgets);
// Vite extracts the imported stylesheet into a sibling bootstrap-bundle.css file.
import 'bootstrap';
import 'bootstrap/dist/css/bootstrap.min.css';
