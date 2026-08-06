import { fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { afterEach, describe, expect, it } from "vitest";
import { AutomaticMaskedIdInput } from "./AutomaticMaskedIdInput";

function Harness() {
  const [value, setValue] = useState("");
  return <AutomaticMaskedIdInput value={value} onChange={setValue} />;
}

afterEach(() => {
  localStorage.clear();
  sessionStorage.clear();
});

describe("AutomaticMaskedIdInput", () => {
  it("accepts keyboard entry and removes the full value from the DOM after blur", async () => {
    const user = userEvent.setup();
    const { container } = render(<Harness />);
    const input = screen.getByLabelText(/^ID reference$/i);

    await user.type(input, "SC12345678");
    expect(input).toHaveValue("SC12345678");
    await user.tab();

    expect(input).toHaveValue("SC****5678");
    expect(container.innerHTML).not.toContain("SC12345678");
    expect(input).toHaveAccessibleDescription(/automatically shows only the first 2 and last 4/i);
    expect(screen.queryByText(/type asterisks|with asterisks/i)).not.toBeInTheDocument();
  });

  it("supports pasted input, deletion, and replacement without revealing the prior value", async () => {
    const user = userEvent.setup();
    render(<Harness />);
    const input = screen.getByLabelText(/^ID reference$/i);

    await user.click(input);
    await user.paste("ABCD1239");
    await user.keyboard("{Backspace}4");
    await user.tab();
    expect(input).toHaveValue("AB**1234");

    await user.click(screen.getByRole("button", { name: /change/i }));
    expect(input).toHaveValue("");
    await user.type(input, "PWD-123456789");
    await user.tab();
    expect(input).toHaveValue("PW*******6789");
  });

  it("clears short and malformed values rather than leaving them visible", async () => {
    const user = userEvent.setup();
    render(<Harness />);
    const input = screen.getByLabelText(/^ID reference$/i);

    await user.type(input, "AB1234");
    await user.tab();
    expect(input).toHaveValue("");
    expect(screen.getByRole("alert")).toHaveTextContent(/at least 7 characters/i);

    await user.click(input);
    fireEvent.change(input, { target: { value: "SC1234ñ5678" } });
    expect(input).toHaveValue("");
    expect(screen.getByRole("alert")).toHaveTextContent(/letters, numbers, and hyphens/i);
  });

  it("does not persist raw or masked values in browser storage", async () => {
    const user = userEvent.setup();
    render(<Harness />);
    const input = screen.getByLabelText(/^ID reference$/i);

    fireEvent.change(input, { target: { value: "SC12345678" } });
    fireEvent.blur(input);

    expect(JSON.stringify(localStorage)).not.toMatch(/SC12345678|SC\*\*\*\*5678/);
    expect(JSON.stringify(sessionStorage)).not.toMatch(/SC12345678|SC\*\*\*\*5678/);
  });
});
