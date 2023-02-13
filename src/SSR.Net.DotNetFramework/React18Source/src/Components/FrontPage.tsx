import React, { useState } from "react";

interface Link {
  text: string;
  href: string;
}

interface Props {
  header?: string;
  links?: Link[];
}

const FrontPage = ({ header, links }: Props) => {
  const [expanded, setExpanded] = useState(false);
  const result = fetch("https://www.google.com");
  return (
    <div>
      <h1>{header}</h1>
      {links && (
        <>
          <button onClick={() => setExpanded(!expanded)}>Expand links</button>
          {expanded && (
            <ul>
              {links.map((link, idx) => (
                <li key={idx}>
                  <a href={link.href}>{link.text}</a>
                </li>
              ))}
            </ul>
          )}
        </>
      )}
    </div>
  );
};

export default FrontPage;
