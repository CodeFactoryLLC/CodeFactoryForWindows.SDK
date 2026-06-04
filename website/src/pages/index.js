import React from 'react';
import {Redirect} from '@docusaurus/router';
import useBaseUrl from '@docusaurus/useBaseUrl';

// The site root redirects straight into the documentation intro.
// Replace this with a custom landing page component when one is designed.
export default function Home() {
  return <Redirect to={useBaseUrl('/docs/intro')} />;
}
