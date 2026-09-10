import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { FiscalReportingPage } from "./FiscalReportingPage";
import { createFiscalReportingFixtureClient, fiscalReportingPermissions } from "./fiscalReporting";

const site={siteId:"77000000-0000-0000-0000-000000000002",sitePosServerId:"88000000-0000-0000-0000-000000000002",displayName:"PITX"};
const permissions=Object.values(fiscalReportingPermissions);

describe("Management Platform fiscal reporting",()=>{
 it("shows exact backend-provided invoice text and an authoritative txt URL",async()=>{
  render(<FiscalReportingPage client={createFiscalReportingFixtureClient()} sites={[site]} scopes={[{scopeType:"SITE",scopeReference:site.siteId,displayName:"PITX"}]} permissions={permissions}/>);
  expect(await screen.findByText("SI-00000001")).toBeInTheDocument();
  expect(screen.getByText((_,element)=>element?.tagName==="PRE"&&element.textContent?.includes("VAT Exempt Sales   : PHP 0.00")===true)).toBeInTheDocument();
  const link=screen.getByRole("link",{name:/download authoritative/i});
  expect(link).toHaveAttribute("href",expect.stringContaining("/v1/management-platform/fiscal-reporting/sites/"));
  expect(link).toHaveAttribute("href",expect.not.stringContaining("blob:"));
 });

 it("generates X through the client and presents it as non-closing",async()=>{
  const client=createFiscalReportingFixtureClient(); client.generateX=vi.fn(client.generateX);
  render(<FiscalReportingPage client={client} sites={[site]} scopes={[]} permissions={permissions}/>);
  await userEvent.click(screen.getByRole("tab",{name:"X Reading"}));
  expect(screen.getByText(/Non-closing observation/)).toBeInTheDocument();
  await userEvent.click(screen.getByRole("button",{name:"Generate X Reading"}));
  await waitFor(()=>expect(client.generateX).toHaveBeenCalledWith(site.siteId));
 });

 it("requires an explicit warning confirmation before Z generation",async()=>{
  const client=createFiscalReportingFixtureClient(); client.generateZ=vi.fn(client.generateZ);
  render(<FiscalReportingPage client={client} sites={[site]} scopes={[]} permissions={permissions}/>);
  await userEvent.click(screen.getByRole("tab",{name:"Z Reading"}));
  await userEvent.click(screen.getByRole("button",{name:"Generate Z Reading"}));
  expect(screen.getByRole("alertdialog",{name:"Confirm Z Reading close"})).toHaveTextContent("closes and finalizes");
  expect(client.generateZ).not.toHaveBeenCalled();
  await userEvent.click(screen.getByRole("button",{name:/Confirm close/}));
  await waitFor(()=>expect(client.generateZ).toHaveBeenCalledWith(site.siteId));
 });

 it("does not expose Z generation from read permission",async()=>{
  render(<FiscalReportingPage client={createFiscalReportingFixtureClient()} sites={[site]} scopes={[]} permissions={[fiscalReportingPermissions.zRead]}/>);
  await userEvent.click(screen.getByRole("tab",{name:"Z Reading"}));
  expect(screen.queryByRole("button",{name:"Generate Z Reading"})).not.toBeInTheDocument();
 });
});
